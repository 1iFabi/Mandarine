using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;

namespace Mandarine;

/// <summary>
/// Lossless metadata removal: copies the compressed image untouched and leaves out EXIF (camera,
/// GPS, dates), XMP, IPTC and comments. JPEG keeps its colour profile and, so photos stay upright,
/// a minimal EXIF holding only the orientation.
/// </summary>
public static class Metadata
{
    public static void Strip(string input, string output)
    {
        var data = File.ReadAllBytes(input);
        var clean = data.AsSpan().StartsWith(PngSignature) ? StripPng(data)
            : data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 ? StripJpeg(data)
            : throw new InvalidOperationException("Solo se limpian metadatos de JPG y PNG.");
        using var f = new FileStream(output, FileMode.CreateNew); // never overwrite
        f.Write(clean);
    }

    static readonly byte[] PngSignature = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };
    static readonly string[] PngMetadataChunks = { "tEXt", "zTXt", "iTXt", "eXIf", "tIME" };

    static byte[] StripPng(byte[] data)
    {
        // shortcut: drops eXIf with its orientation; PNG viewers rarely honour it, revisit if photos turn up sideways.
        using var o = new MemoryStream(data.Length);
        o.Write(PngSignature);
        for (var at = PngSignature.Length; at + 12 <= data.Length;)
        {
            var size = 12 + (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
            if (size < 12 || at + size > data.Length) throw new InvalidDataException("PNG dañado.");
            var type = System.Text.Encoding.ASCII.GetString(data, at + 4, 4);
            if (!PngMetadataChunks.Contains(type)) o.Write(data, at, size);
            at += size;
        }
        return o.ToArray();
    }

    static byte[] StripJpeg(byte[] data)
    {
        using var o = new MemoryStream(data.Length);
        o.Write(data, 0, 2); // SOI
        ushort orientation = 1;
        var wroteOrientation = false;
        var at = 2;
        while (at + 4 <= data.Length)
        {
            if (data[at] != 0xFF) throw new InvalidDataException("JPEG dañado.");
            var marker = data[at + 1];
            if (marker == 0xFF) { at++; continue; } // fill byte
            if (marker == 0xDA) break;              // start of scan: the rest is image data
            var size = 2 + BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at + 2));
            if (at + size > data.Length) throw new InvalidDataException("JPEG dañado.");
            var segment = data.AsSpan(at, size);

            if (marker == 0xE1 && segment[4..].StartsWith("Exif\0\0"u8)) orientation = ReadOrientation(segment[10..].ToArray());
            // Kept: APP0 JFIF, APP2 ICC profile, APP14 Adobe colour transform, and everything that is not APPn/COM.
            var drop = marker is 0xE1 or (>= 0xE3 and <= 0xED) or 0xEF or 0xFE;
            if (!drop)
            {
                // The orientation goes after JFIF (which must come first) and before the frame.
                if (!wroteOrientation && marker != 0xE0) { WriteOrientation(o, orientation); wroteOrientation = true; }
                o.Write(segment);
            }
            at += size;
        }
        if (!wroteOrientation) WriteOrientation(o, orientation);
        o.Write(data, at, data.Length - at);
        return o.ToArray();
    }

    /// <summary>A JPEG's EXIF orientation (1 = upright, 6 = rotate 90° clockwise, ...).</summary>
    public static ushort Orientation(string jpeg)
    {
        var data = File.ReadAllBytes(jpeg);
        for (var at = 2; at + 4 <= data.Length && data[at] == 0xFF && data[at + 1] != 0xDA;)
        {
            var size = 2 + BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at + 2));
            if (at + size > data.Length) break;
            if (data[at + 1] == 0xE1 && data.AsSpan(at + 4).StartsWith("Exif\0\0"u8)) return ReadOrientation(data[(at + 10)..(at + size)]);
            at += size;
        }
        return 1;
    }

    /// <summary>Tag 0x0112 from IFD0 of a TIFF block; 1 (upright) when absent or unreadable.</summary>
    static ushort ReadOrientation(byte[] tiff)
    {
        if (tiff.Length < 8) return 1;
        var le = tiff[0] == (byte)'I';
        ushort U16(int i) => le ? BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(i)) : BinaryPrimitives.ReadUInt16BigEndian(tiff.AsSpan(i));
        uint U32(int i) => le ? BinaryPrimitives.ReadUInt32LittleEndian(tiff.AsSpan(i)) : BinaryPrimitives.ReadUInt32BigEndian(tiff.AsSpan(i));
        var ifd = U32(4);
        if (ifd + 2 > tiff.Length) return 1;
        var count = U16((int)ifd);
        for (var e = 0; e < count; e++)
        {
            var entry = (int)ifd + 2 + 12 * e;
            if (entry + 12 > tiff.Length) return 1;
            if (U16(entry) == 0x0112) return U16(entry + 8) is >= 1 and <= 8 and var v ? v : (ushort)1;
        }
        return 1;
    }

    static void WriteOrientation(Stream o, ushort orientation)
    {
        if (orientation == 1) return;
        // APP1 "Exif" + big-endian TIFF with a single IFD0 entry: Orientation (SHORT, 1 value).
        Span<byte> app1 = stackalloc byte[]
        {
            0xFF, 0xE1, 0x00, 0x22, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
            0x00, 0x01, 0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, (byte)orientation, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        };
        o.Write(app1);
    }
}
