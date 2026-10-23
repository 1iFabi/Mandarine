# Product

<!-- impeccable:product-schema 1 -->

## Platform

windows (native desktop app, WPF on .NET 8; not one of web/ios/android/adaptive)

## Users
Office workers who process documents and pictures several times a day: client PDFs, reports, screenshots for presentations. They drag a file, pick an operation and expect it done in seconds; precision and keyboard shortcuts matter to them.

## Product Purpose
Mandarine converts, edits and shares files from a radial wheel that appears while a file is being dragged (Shift = convert, Ctrl = tools, Alt = actions). Results are saved beside the original, never over it. Success: the job is done without opening another application.

## Positioning
The gesture: no app to open, no file dialog. The wheel appears at the cursor mid-drag and the work happens offline, on the PC, with bundled tools (FFmpeg, the craft converters, Tesseract, BiRefNet).

## Operating Context
Windows 10 2004+ / Windows 11, 64-bit, often beside Explorer, a browser and Office. Editors open as separate windows after a drop: picture editors (crop, annotate, hide, adjust, background, rotate) and the PDF page window (rotate pages, split by ranges, a page reader). Some editors apply one setting to many files; others go file by file.

## Capabilities and Constraints
- Everything runs locally; there is no network code.
- Interface text is Spanish; English is planned, not built.
- Editors must keep Enter = save, Esc = cancel, Ctrl+Z = undo where it applies.
- Next features the user asked for: resize and watermark for pictures; trimming for video and audio.

## Brand Commitments
- Accent orange #FF5200, shared with the wheel and the tangerine logo (`src/Mandarine/Assets/mandarine.png`).
- It must feel like a Windows app (Windows 11 fonts, controls and behaviour), not a macOS clone.
- Name and look are Mandarine's own; nothing is taken from Tangerine for Mac.

## Evidence on Hand
README screenshots of the wheel in `docs/images/`. No users, testimonials or usage data exist; none may be invented.

## Product Principles
- Seconds, not minutes: the editor opens on the job, with sensible defaults, and closes on Enter.
- Never destroy: originals are untouched; every output is a new file beside them.
- Precision over decoration: exact values and shortcuts are first-class for people who do this daily.
- Local and private by construction.
