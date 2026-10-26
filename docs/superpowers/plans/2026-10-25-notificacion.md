# Notificación al terminar · plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reemplazar el globo de Windows por una notificación propia (opción C) que dice qué se hizo y dónde quedó, con "Abrir carpeta".

**Architecture:** Un modelo puro `Notice` arma el texto a partir de las rutas de salida o de los errores. Una ventana WPF `NoticeWindow` lo dibuja abajo a la derecha, sin activarse, como la rueda. `App.Notify` pasa a usarla, y `Editor.Run` devuelve rutas en vez de un conteo.

**Tech Stack:** C# .NET 8 WPF (`net8.0-windows10.0.19041.0`), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-25-notificacion-design.md`

## Global Constraints

- Fondo #1C1C1E, texto #F2F2F7, secundario #A1A1A6, enlace #FF6A24, franja de 4 px #FF5200 (éxito) o #D93025 (error), esquinas de 8 px, Segoe UI Variable Text 13 (un solo tamaño, la jerarquía va por peso).
- Abajo a la derecha del área de trabajo (`SystemParameters.WorkArea`), con un margen de 16 DIP.
- Ventana `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, `Topmost`, `ShowActivated=False`, `ShowInTaskbar=False`: nunca roba el foco.
- Se cierra a los 5 s; con el mouse encima se queda; un clic en la línea la cierra.
- Textos de la UI en español.
- No usar las notificaciones nativas de Windows.

## Review Focus

- **Nombre largo** (`informe-final-revisado-version-3-cliente.pdf`): la línea no debe pasar de 480 DIP; el nombre se recorta con "…" y "Abrir carpeta" sigue visible. Test en la Task 2.
- **Segunda notificación mientras hay otra visible:** reemplaza el contenido y reinicia los 5 s, no apila ventanas. Test en la Task 2.
- **Salidas en carpetas distintas** (varios archivos de orígenes distintos): "en" usa la carpeta de la primera salida, y "Abrir carpeta" selecciona esa primera salida. Test en la Task 1.
- **Salida que es una carpeta** (dividir un PDF en varias partes da una carpeta): "Abrir carpeta" abre esa carpeta y el nombre es el de la carpeta. Test en la Task 1.
- **Editor cancelado tras guardar algunos:** se notifican solo los guardados; con cero no hay notificación. Test en la Task 3.

---

### Task 1: Modelo `Notice`

**Files:**
- Create: `src/Mandarine/Notice.cs`
- Test: `tests/Mandarine.Tests/NoticeTests.cs`

**Interfaces:**
- Produces: `public sealed record Notice(bool IsError, string Title, string Detail, string? Reveal)`
  - `static Notice Done(IReadOnlyList<string> outputs)`: `Title` = el nombre del archivo, o "3 archivos" si hay varios. `Detail` = "en <nombre de la carpeta de outputs[0]>". `Reveal` = outputs[0].
  - `static Notice Failed(int failed, int total, string detail, string? file)`: con `total == 1`, `Title` = "No se pudo convertir <file>"; si no, `Title` = "Fallaron <failed> de <total>". `Detail` = la primera línea de `detail`, recortada a 120 caracteres con "…". `Reveal` = null.

- [ ] **Step 1: Write the failing tests** in `NoticeTests.cs`:
  - `Done_one_file_names_it_and_its_folder`: `Done(["C:\\Users\\a\\Videos\\clip.mkv"])` → `Title == "clip.mkv"`, `Detail == "en Videos"`, `Reveal` es la ruta, `IsError == false`.
  - `Done_many_counts_them`: tres rutas en `C:\\x\\Fotos\\` → `Title == "3 archivos"`, `Detail == "en Fotos"`.
  - `Done_mixed_folders_uses_the_first`: rutas en `C:\\a\\Uno\\` y `C:\\b\\Dos\\` → `Detail == "en Uno"`, `Reveal` es la primera.
  - `Done_folder_output_names_the_folder`: `Done(["C:\\x\\Docs\\informe partes"])` → `Title == "informe partes"`, `Detail == "en Docs"`.
  - `Failed_single_and_many`: `Failed(1,1,"ffmpeg: códec\nmás","reunion.mov")` → `Title == "No se pudo convertir reunion.mov"`, `Detail == "ffmpeg: códec"`, `IsError`. `Failed(2,5,"x",null)` → `Title == "Fallaron 2 de 5"`.
- [ ] **Step 2:** `dotnet test --filter NoticeTests` → FAIL (no compila: `Notice` no existe).
- [ ] **Step 3:** Implementar `Notice` en `src/Mandarine/Notice.cs` (`Path.GetFileName` y `Path.GetDirectoryName`, sin tocar el disco).
- [ ] **Step 4:** `dotnet test --filter NoticeTests` → PASS.

### Task 2: Ventana `NoticeWindow`

**Files:**
- Create: `src/Mandarine/NoticeWindow.cs` (en código, sin XAML: es una sola línea)
- Test: `tests/Mandarine.Tests/NoticeWindowTests.cs`

**Interfaces:**
- Consumes: `Notice` (Task 1).
- Produces: `public sealed class NoticeWindow : Window` con `public void Present(Notice n)`, `public string ShownTitle { get; }` y `public int TimerResets { get; }` (para los tests). "Abrir carpeta" corre `explorer.exe /select,"<Reveal>"` (o `explorer.exe "<Reveal>"` si es una carpeta), igual que `Actions.cs:50`, y cierra. El enlace se oculta si `Reveal` es null.

- [ ] **Step 1: Write the failing tests** (hilo STA, como en `WheelPreviewTests`):
  - `Present_shows_title_and_hides_after_click`: `Present(Done(["C:\\x\\V\\a.mkv"]))` → `IsVisible`, `ShownTitle == "a.mkv"`. Simular el clic en la línea (`RaiseEvent(MouseLeftButtonUp)` sobre el contenido) → `!IsVisible`.
  - `Second_notice_replaces_and_restarts`: dos `Present` seguidos → `ShownTitle` es el segundo, `TimerResets == 2`, y sigue habiendo una sola ventana (misma instancia visible).
  - `Long_name_stays_within_480`: título de 90 caracteres → tras `UpdateLayout()`, `ActualWidth <= 480`.
- [ ] **Step 2:** `dotnet test --filter NoticeWindowTests` → FAIL.
- [ ] **Step 3:** Implementar `NoticeWindow`: un `Border` con la franja (`BorderThickness` 4,0,0,0), un `TextBlock` con ✓/✗, el título en SemiBold, el detalle en #A1A1A6 y el enlace. `MaxWidth` 480 y `TextTrimming.CharacterEllipsis` en el título. `DispatcherTimer` de 5 s que se detiene en `MouseEnter` y se reinicia en `MouseLeave`. Estilos extendidos en `OnSourceInitialized`, como `WheelWindow.xaml.cs:85-91`. Posición: `WorkArea.Right - ActualWidth - 16`, `WorkArea.Bottom - ActualHeight - 16`, después de medir.
- [ ] **Step 4:** `dotnet test --filter NoticeWindowTests` → PASS.

### Task 3: `Editor.Run` devuelve rutas

**Files:**
- Modify: `src/Mandarine/EditorWindow.xaml.cs:20-92`
- Test: `tests/Mandarine.Tests/EditorTests.cs`

**Interfaces:**
- Produces: `public static Task<IReadOnlyList<string>> Run(Format tool, string[] files)`: las rutas que se escribieron, en orden; vacía si se canceló antes de guardar.
- Calcular cada ruta **una sola vez** con `Converter.OutputPath(...)` y usar esa misma variable para guardar y para devolverla: `OutputPath` puede elegir otro nombre libre si se lo llama de nuevo después de guardar. `Rotate` y `Pages` devuelven el `string` de `Converter.ConvertAsync`. `ScanPages` devuelve la ruta del PDF.

- [ ] **Step 1: Write the failing test** `Run_returns_saved_paths`: con el seam de prueba que ya usa `EditorTests` (o, si no lo hay, probar a través de `Rotate`/`Pages`, que no abren diálogo con un `Format` de prueba), comprobar que el resultado contiene la ruta escrita y que `File.Exists` es verdadero en cada una. Si una ruta no se puede probar sin diálogo, documentarlo en el test con `// shortcut:`.
- [ ] **Step 2:** `dotnet test --filter EditorTests` → FAIL (el tipo de retorno es `int`).
- [ ] **Step 3:** Cambiar las 5 funciones (`Run`, `Rotate`, `ScanPages`, `Pages` y el bucle compartido) a `List<string>`.
- [ ] **Step 4:** `dotnet test` → PASS (todo el proyecto, porque cambia una firma pública).

### Task 4: Conectar `App`

**Files:**
- Modify: `src/Mandarine/App.xaml.cs:21-28, 80-114`

**Interfaces:**
- Consumes: `Notice.Done/Failed` (Task 1), `NoticeWindow.Present` (Task 2), `Editor.Run` con rutas (Task 3).

- [ ] **Step 1:** Crear un `NoticeWindow _notice` junto a `_wheel` en `OnStartup`.
- [ ] **Step 2:** `ConvertAll` junta las rutas que devuelven `ConvertAsync`/`CombineAsync`. Sin errores → `Present(Notice.Done(outputs))`. Con errores → `Present(Notice.Failed(errors.Count, files.Length (1 si `Combines`), string.Join("\n", errors), files.Length == 1 ? Path.GetFileName(files[0]) : null))`.
- [ ] **Step 3:** `EditAll`: si hay rutas, `Done(paths)`; si lanza una excepción, `Failed(1, 1, ex.Message, Path.GetFileName(files[0]))`.
- [ ] **Step 4:** El error de las acciones (`App.xaml.cs:27`) → `Failed(1, 1, ex.Message, null)`, con el título "No se pudo <acción>": agregar a `Failed` el parámetro opcional `string verb = "convertir"` y pasar `format.Label.ToLowerInvariant()`.
- [ ] **Step 5:** Dejar `Notify` con el globo **solo** para "Mandarine está listo" (lo reemplaza el "Cómo se usa" en otro plan) y borrar sus otros usos.
- [ ] **Step 6:** `dotnet build` sin advertencias nuevas y `dotnet test` → todo PASS.
- [ ] **Step 7: Prueba manual.** Cerrar la Mandarine de desarrollo, compilar y abrirla. Convertir un video a MKV: aparece la línea "✓ <archivo>.mkv en <carpeta>", y "Abrir carpeta" selecciona el archivo. Forzar un error, por ejemplo con un archivo dañado renombrado a `.mp4`: aparece la franja roja. Pasar el mouse por encima: no se va.
