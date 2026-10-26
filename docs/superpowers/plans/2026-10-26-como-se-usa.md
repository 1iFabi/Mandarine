# "Cómo se usa" con práctica · plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Una ventana "Cómo se usa" para practicar con una foto de ejemplo y la rueda real. Se abre la primera vez y desde el menú del ícono, y reemplaza el globo de Windows.

**Architecture:** `Practice` (estático) maneja la carpeta y la foto de práctica, sabe a qué paso corresponde cada entrada de la rueda y si ya se vio la guía (registro). `GuideWindow` reutiliza `EditorPane` (panel blanco + pestaña manila) y lanza el arrastre de la foto con `DragDrop.DoDragDrop`. `App` marca pasos cuando un trabajo sobre la foto de práctica termina bien.

**Tech Stack:** C# .NET 8 WPF, xUnit, registro `HKCU\Software\Mandarine`.

**Spec:** `docs/superpowers/specs/2026-10-26-como-se-usa-design.md`

## Global Constraints

- Estilo de `DESIGN.md` / `EditorStyles.xaml`: Ground #F3F3F3, Accent #FF5200, Ink #1C1C1E, Muted #5E5E63, Line #E5E5E5, Control #D6D6D6, un solo tamaño de texto (13), sin gradientes en la interfaz (el degradado está solo en la foto de ejemplo).
- Textos exactos: título "Cómo se usa Mandarine"; subtítulo "Practica con esta foto: no toca tus archivos"; pasos "Convertir" / "Herramientas" / "Acciones" con las teclas Shift / Ctrl / Alt; nota "Soltar en el centro o Esc cancela. Lo nuevo queda al lado del original, nunca encima."; pestaña "n de 3 listo"; botones "Saltar" y "Listo"; entrada de menú "Cómo se usa".
- Carpeta de práctica: `%TEMP%\Mandarine\Práctica`; foto `mandarina.jpg`.
- Registro: valor `GuideShown` (DWORD 1) en `HKCU\Software\Mandarine`. Solo Listo y Saltar lo escriben; la X no.
- Solo las entradas usadas sobre la foto de práctica marcan pasos.

## Review Focus

- **"Mover" o "Copiar" con la foto de práctica:** el paso Acciones se marca, y la foto vuelve a existir para seguir practicando. Test en la Task 1 (`EnsurePhoto` la recrea) y en la Task 3 (después de marcar se llama a `EnsurePhoto`).
- **Cancelar (soltar en el centro, Esc, cerrar un editor):** no marca nada. Test en la Task 3.
- **Conversión fallida sobre la foto:** no marca el paso. Test en la Task 3.
- **Abrir "Cómo se usa" desde el menú con la ventana ya abierta:** la trae al frente, sin abrir una segunda. Test en la Task 3.
- **Archivos propios mezclados con la foto en el mismo arrastre:** cuenta igual, porque la foto participó. Test en la Task 1 (`Involves`).

---

### Task 1: `Practice`

**Files:**
- Create: `src/Mandarine/Practice.cs`
- Test: `tests/Mandarine.Tests/PracticeTests.cs`

**Interfaces:**
- Produces:
  - `public static string Folder` → `Path.Combine(Path.GetTempPath(), "Mandarine", "Práctica")`
  - `public static string EnsurePhoto()` → crea la carpeta y `mandarina.jpg` si faltan (dibuja el logo `Assets/mandarine.png` sobre un degradado naranja #FFB067→#FF5200, 1200×800, con `ImageEdit.Save`); devuelve la ruta. Debe correr en un hilo STA.
  - `public static bool Involves(IEnumerable<string> files)` → alguna ruta es la foto de práctica (sin distinguir mayúsculas).
  - `public static int StepFor(Format f)` → 2 si `Engine.Action`; 1 si `f.Icon != null` (herramienta); si no, 0.
  - `public static bool IsSeen(RegistryKey key)` / `public static void MarkSeen(RegistryKey key)` sobre el valor `GuideShown`.

- [ ] **Step 1: Write the failing tests:**
  - `EnsurePhoto_creates_and_recreates`: en STA, `EnsurePhoto()` → el archivo existe y mide > 10 KB; borrarlo y volver a llamar → existe de nuevo.
  - `Involves_finds_the_photo_among_others`: `Involves([@"C:\x\mio.png", Path.Combine(Folder, "MANDARINA.JPG")])` → true; `Involves([@"C:\x\mio.png"])` → false.
  - `StepFor_maps_wheels`: con `Catalog`, una acción ("Copiar") → 2, una herramienta de foto ("Recortar") → 1, un formato ("PNG") → 0.
  - `Seen_is_stored_in_registry`: sobre `Registry.CurrentUser.CreateSubKey(@"Software\Mandarine\Test-<guid>")`, `IsSeen` → false; `MarkSeen` → `IsSeen` true; borrar la subclave al final.
- [ ] **Step 2:** `dotnet test --filter PracticeTests` → FAIL (`Practice` no existe).
- [ ] **Step 3:** Implementar `Practice.cs`.
- [ ] **Step 4:** `dotnet test --filter PracticeTests` → PASS.

### Task 2: `GuideWindow`

**Files:**
- Create: `src/Mandarine/GuideWindow.xaml`, `src/Mandarine/GuideWindow.xaml.cs`
- Test: `tests/Mandarine.Tests/GuideWindowTests.cs`

**Interfaces:**
- Consumes: `Practice.EnsurePhoto`, `EditorPane` (`Show(tool, file, progress)`, `Output(path, detail)`, `Add(caption, control)`, `CancelButton`, `SaveButton`).
- Produces: `public sealed partial class GuideWindow : Window` con:
  - `public void MarkDone(int step)` (0..2, repetido no hace nada)
  - `public int DoneCount { get; }`, `public int Current { get; }` (primer paso que falta; 3 si están todos)
  - `public string TabText { get; }` ("n de 3 listo")
  - `public event Action? Finished` (Listo o Saltar; la X no lo dispara)
- Layout como la maqueta: columna izquierda Ground con la foto (miniatura de 96×72 + nombre) y la pista del paso actual ("Arrastra la foto y, sin soltar, toca **Shift**/**Ctrl**/**Alt**"; con todo listo: "¡Listo! Ya sabes usar Mandarine"); a la derecha `EditorPane` con `Show("Cómo se usa Mandarine", "Practica con esta foto: no toca tus archivos", "")`, los 3 pasos en `Body` y la nota. Botones de la pestaña: `CancelButton.Content = "Saltar"`, `SaveButton.Content = "Listo"`. Punto de cada paso: gris (Control), borde Accent si es el actual, Accent con "✓" si está hecho.
- Arrastre: `MouseMove` con el botón apretado sobre la foto → `DragDrop.DoDragDrop(foto, new DataObject(DataFormats.FileDrop, new[] { Practice.EnsurePhoto() }), DragDropEffects.Copy | DragDropEffects.Move)`.

- [ ] **Step 1: Write the failing tests** (STA):
  - `Steps_mark_in_any_order`: nueva → `DoneCount == 0`, `Current == 0`, `TabText == "0 de 3 listo"`. `MarkDone(1)` → `Current == 0`, `TabText == "1 de 3 listo"`. `MarkDone(0)`, `MarkDone(0)` → `DoneCount == 2`, `Current == 2`. `MarkDone(2)` → `Current == 3`.
  - `Skip_and_done_finish_but_close_does_not`: suscribir `Finished`; `Close()` → no se disparó. En otra ventana, `CancelButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent))` → se disparó una vez.
  - `Renders_guide_preview`: como `WheelPreviewTests`, renderizar la ventana con el paso 0 hecho a `%TEMP%\mandarine-guide.png` (> 5 KB) para revisarla a ojo.
- [ ] **Step 2:** `dotnet test --filter GuideWindowTests` → FAIL.
- [ ] **Step 3:** Implementar `GuideWindow`.
- [ ] **Step 4:** `dotnet test --filter GuideWindowTests` → PASS. Abrir `%TEMP%\mandarine-guide.png` y compararlo con la maqueta.

### Task 3: Conectar `App`

**Files:**
- Modify: `src/Mandarine/App.xaml.cs`
- Test: `tests/Mandarine.Tests/GuideWindowTests.cs`

**Interfaces:**
- Consumes: Task 1 y Task 2.
- Produces: `internal void Practiced(string[] files, Format format)` en `App`, con la lógica en `public static bool GuideWindow.Apply(GuideWindow? g, string[] files, Format f)`, para poder probarla sin `App`: si `g` está abierta y `Practice.Involves(files)`, `MarkDone(Practice.StepFor(f))`, `Practice.EnsurePhoto()` y devuelve true.

- [ ] **Step 1: Write the failing tests:**
  - `Apply_marks_only_practice`: `Apply(g, [mio.png], fmt)` → false y `DoneCount == 0`. `Apply(g, [foto], Copiar)` → true, `DoneCount == 1` y la foto existe (aunque se haya borrado antes del `Apply`).
  - `Apply_ignores_closed_guide`: `Apply(null, [foto], fmt)` → false.
- [ ] **Step 2:** `dotnet test --filter GuideWindowTests` → FAIL.
- [ ] **Step 3:** Implementar `Apply`. En `App`: llamar a `Practiced` **solo en caso de éxito** en `ConvertAll` (sin errores), en `EditAll` (con `saved.Count > 0`) y después de `Actions.Run` sin excepción; nunca si se canceló. Agregar el menú "Cómo se usa" como primera entrada, y que abra la guía o, si ya está abierta, la active (`Activate()`). Al iniciar, si `!Practice.IsSeen(key)`, abrirla. `Finished` → `Practice.MarkSeen` y cerrar. Borrar `Notify`, el globo y el aviso de inicio.
- [ ] **Step 4:** `dotnet test` → todo PASS y `dotnet build` sin advertencias nuevas.
- [ ] **Step 5: Prueba manual (la hace la persona):** borrar `GuideShown` del registro, abrir Mandarine → se abre la guía. Arrastrar la foto y tocar Shift → **sale la rueda** (el riesgo del spec: el arrastre parte de la propia ventana). Soltar en WEBP → notificación y paso 1 ✓. Repetir con Ctrl y Alt. Listo → no vuelve a abrirse al reiniciar; el menú "Cómo se usa" la abre.
