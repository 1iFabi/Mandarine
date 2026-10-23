---
version: 1
slug: "src-mandarine-editorwindow-xaml"
primary_target: "src/Mandarine/EditorWindow.xaml"
related_targets: ["src/Mandarine/PdfPagesWindow.xaml"]
---

Scope: the editor windows opened after a drop: picture editors (Recortar, Anotar, Ocultar, Ajustes, Fondo, Rotar) and the PDF page window (Rotar, Dividir, lector). Mode: Operate.
Audience and task: office workers fixing a picture or PDF several times a day; they set an option, check the result and save in seconds, often by keyboard.
Constraints: Windows 11 feel (Segoe UI Variable, native control behaviour), accent #FF5200, Spanish copy, Enter/Esc/Ctrl+Z kept, the Mandarine logo visible.

## Direction contract
THESIS: The editor is a folder: the work lies on top and its index tab names the file you are about to make. Refuses the stock toolbar-over-dark-canvas photo editor.
OWN-WORLD: Mica-light ground #F3F3F3, white inspector pane as the only raised sheet, ink #1C1C1E, manila #E7D3A8 reserved for the output tab, #FF5200 for the primary action, selection and focus. One text size in the pane; rank by weight. Tabular figures with units.
STORY: The user sees the picture large, adjusts in the pane, reads on the tab exactly what will be written (name, size, folder) and presses Enter.
FIRST VIEWPORT: Title bar with the tangerine logo, tool name and file name. Canvas fills the left; 320 px inspector on the right: tool title, option groups, numeric fields; at its foot the manila output tab holding Cancelar and Guardar.
FORM: Manila filing folder with an index tab, candidate 7 of 7. seed:de7b3ca92e8e8a65
FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
