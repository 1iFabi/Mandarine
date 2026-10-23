# Third-party notices

## wordcraft, gridcraft, deckcraft, pdfcraft

Document, spreadsheet, presentation and PDF work runs the unmodified CLIs from
https://github.com/storytold/wordcraft (v0.9.0), https://github.com/storytold/gridcraft (v0.10.0),
https://github.com/storytold/deckcraft (v0.9.0) and https://github.com/storytold/pdfcraft (v0.11.0), fetched by `scripts/fetch-tools.ps1` into `tools/<name>/`.
Each is dual-licensed Apache-2.0 / MIT; the license texts ship beside each executable (`LICENSE-APACHE`, `LICENSE-MIT`),
as do the SIL Open Font License texts for the fonts they embed (`OFL-*.txt`) and pdfcraft's `NOTICE`.

## Background removal and OCR

- **ONNX Runtime** (https://github.com/microsoft/onnxruntime), MIT, NuGet `Microsoft.ML.OnnxRuntime`: runs the background-removal model.
- **BiRefNet** (https://github.com/ZhengPeng7/BiRefNet), MIT: the `general-lite` model in `tools/models/birefnet-lite.onnx`,
  in the ONNX export published by rembg (https://github.com/danielgatis/rembg, MIT).
- **Tesseract** (https://github.com/tesseract-ocr/tesseract), Apache-2.0, through the .NET wrapper https://github.com/charlesw/tesseract
  (Apache-2.0, NuGet `Tesseract`): reads scanned PDFs.
- **tessdata_best** (https://github.com/tesseract-ocr/tessdata_best), Apache-2.0: `spa.traineddata` and `eng.traineddata` in `tools/tessdata/`.

## FFmpeg

Images, audio and video run the unmodified `ffmpeg.exe` from FFmpeg 9.0.1 (https://ffmpeg.org), in the "essentials"
Windows build by gyan.dev (https://www.gyan.dev/ffmpeg/builds/, published at https://github.com/GyanD/codexffmpeg/releases/tag/9.0.1),
fetched by `scripts/fetch-tools.ps1` into `tools/ffmpeg/`. That build is licensed under the **GNU GPL version 3**: its text ships
beside the executable (`tools/ffmpeg/LICENSE`), and `tools/ffmpeg/README.txt` lists the libraries it includes (x264, libvpx,
libaom, LAME, Opus, Vorbis, libwebp and others) with their own licenses. Mandarine starts it as a separate program and does not
link to it. The corresponding source code is available at https://ffmpeg.org/releases/ffmpeg-9.0.1.tar.xz and, for the build
configuration and bundled libraries, at https://www.gyan.dev/ffmpeg/builds/.
