# "Cómo se usa" animada · plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** La guía pasa a mostrar un paso a la vez, a lo ancho, con una demostración animada del gesto que se detiene cuando la persona toma la foto para practicar.

**Architecture:** Hay tres piezas.
- `DemoScript`: los datos de cada paso, sacados del catálogo real.
- `DemoTimeline`: una función pura que dice qué se ve en cada instante (pose).
- `GuideDemo`: un `Canvas` que dibuja esa pose, con resortes para la rueda.

`GuideWindow` cambia de forma (barra, escenario, pie manila) y suma el flujo mirar → practicar → ✓ → siguiente. Se mantiene su API de práctica (`Apply`, `MarkDone`, `Finished`…).

**Tech Stack:** C# .NET 8 WPF, xUnit, `Spring`/`SpringClock` existentes.

**Spec:** `docs/superpowers/specs/2026-10-26-guia-animada-design.md`

## Global Constraints

- Ventana de 860×600, `ResizeMode="NoResize"`, sin `EditorPane`.
- Tokens de `EditorStyles.xaml`: Ground #F3F3F3, Accent #FF5200, Ink #1C1C1E, Muted #5E5E63, Line #E5E5E5, Control #D6D6D6, Manila #E7D3A8 / ManilaInk #5A4520 / ManilaLine #CBB78C. Un solo tamaño de texto: 13.
- Sin gradientes ni sombras decorativas. Excepciones: el segmento naranja de la rueda de la demo usa el mismo `Accent` (degradado) de la rueda real, y la copia que se arrastra lleva sombra porque flota.
- Textos exactos:
  - Título: "Cómo se usa Mandarine". Subtítulo: "Tres gestos, uno a la vez".
  - Pasos: "Convertir · Shift", "Herramientas · Ctrl", "Acciones · Alt".
  - Qué hace cada paso: "Apunta a un formato y suelta", "Comprimir, recortar, quitar el fondo…", "Copiar, mover, compartir…".
  - Pie mientras mira: "Mira cómo se hace, o toma la foto para probar".
  - Pie al practicar: "Ahora tú: arrastra la foto y, sin soltar, toca {Key}".
  - Pie al terminar todo: "¡Listo! Ya sabes usar Mandarine".
  - Botones: "Saltar", "Siguiente", y "Listo" en el último paso.
- Elecciones de la demo:

  | Paso | Elige | Archivo nuevo | Aviso |
  |---|---|---|---|
  | 0 | "PNG" | "mandarina.png" | "mandarina.png en Práctica" |
  | 1 | "Sin fondo" | "mandarina-sin-fondo.png" | "mandarina-sin-fondo.png en Práctica" |
  | 2 | "Copiar" | ninguno | "Copiado: pégalo donde quieras" |

- La demo muestra a lo más 8 opciones.
- Con las animaciones de Windows apagadas (`!SystemParameters.ClientAreaAnimation`) se muestra `DemoTimeline.KeyFrame` quieto.
- Sin commits: el usuario hace commit al final. Cada tarea termina con la suite verde.

## Review Focus

- **El catálogo cambia y ya no tiene "Sin fondo" o "Copiar":** la demo elige la primera opción y no se cae. Test en la Task 1.
- **El gesto real sale bien mientras la demo está corriendo** (alguien arrastra su propio archivo y no la foto): eso no cuenta, porque solo `Apply` con la foto marca pasos. Si llega `MarkDone` del paso mostrado sin estar practicando, igual celebra y avanza. Test en la Task 3.
- **`MarkDone` llega para un paso distinto del que se muestra:** se marca, pero el paso mostrado no cambia. Test en la Task 3.
- **Se cierra la ventana con la demo corriendo:** el reloj se detiene y no quedan handlers de `CompositionTarget.Rendering`. Test en la Task 2 (`Stop` deja `Playing == false`) y en la Task 3 (`Closed` llama a `Stop`).
- **"Siguiente" con todos los pasos hechos o en el último paso:** dispara `Finished` una sola vez. Test en la Task 3.

---

### Task 1: `DemoScript` y `DemoTimeline`

**Files:**
- Create: `src/Mandarine/GuideDemo.cs` (aquí van los dos tipos puros; `GuideDemo` se agrega en la Task 2)
- Test: `tests/Mandarine.Tests/GuideDemoTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record DemoStep(string Key, string Name, string What, IReadOnlyList<string> Options, int Pick, string? Output, string Notice)`.
  - `public static class DemoScript { public static DemoStep For(int step); }`. Las opciones salen de `Catalog.TargetsFor` / `ToolsFor` / `ActionsFor(new[] { "mandarina.jpg" })` por `Label`, `Take(8)`. `Pick` es el índice de la etiqueta elegida, o 0 si no está.
  - `public readonly record struct DemoPose(Vector Cursor, bool Pressed, bool Dragging, bool KeyLit, bool WheelOpen, bool Hot, bool Result)`. `Cursor` es el desplazamiento desde el centro de la foto, en DIP.
  - `public static class DemoTimeline`:
    - `const double Loop = 6.0`.
    - `static readonly Vector Rest = new(300, 150)` y `Wheel = new(180, -20)`.
    - `static DemoPose At(double t, DemoStep step)`.
    - `static DemoPose KeyFrame(DemoStep step)`.
    - `static Vector PickPoint(DemoStep step)`: `Wheel` + 64 DIP en el ángulo medio del segmento `Pick`, con el mismo reparto de ángulos que `WheelWindow` (`-π/2 + i·2π/n`).

- [ ] **Step 1: Write the failing tests** en `GuideDemoTests`:

```csharp
[Fact] public void Script_comes_from_the_catalog()
{
    var s = DemoScript.For(0);
    Assert.Equal(("Shift", "Convertir · Shift"), (s.Key, $"{s.Name} · {s.Key}"));
    Assert.Equal(Catalog.TargetsFor(new[] { "mandarina.jpg" }).Select(f => f.Label).Take(8), s.Options);
    Assert.Equal(("PNG", "mandarina.png"), (s.Options[s.Pick], s.Output));
    Assert.Equal("Sin fondo", DemoScript.For(1).Options[DemoScript.For(1).Pick]);
    var copy = DemoScript.For(2);
    Assert.Equal(("Copiar", null, "Copiado: pégalo donde quieras"), (copy.Options[copy.Pick], copy.Output, copy.Notice));
    Assert.All(new[] { 0, 1, 2 }, i => Assert.InRange(DemoScript.For(i).Options.Count, 1, 8));
}

[Fact] public void Timeline_tells_the_gesture_in_order()
{
    var s = DemoScript.For(0);
    Assert.Equal(DemoTimeline.Rest, DemoTimeline.At(0.2, s).Cursor);
    Assert.True(DemoTimeline.At(1.3, s).Pressed);
    Assert.True(DemoTimeline.At(2.6, s).KeyLit);
    var pointing = DemoTimeline.At(3.8, s);
    Assert.True(pointing is { Dragging: true, WheelOpen: true, Hot: true, KeyLit: false });
    Assert.Equal(DemoTimeline.PickPoint(s), pointing.Cursor);
    var after = DemoTimeline.At(4.6, s);
    Assert.True(after is { Result: true, WheelOpen: false, Dragging: false, Pressed: false });
    Assert.Equal(DemoTimeline.At(1.0, s), DemoTimeline.At(1.0 + DemoTimeline.Loop, s));
    Assert.True(DemoTimeline.KeyFrame(s) is { WheelOpen: true, Hot: true, KeyLit: true, Dragging: true });
}
```

  Además, un test que use un `DemoStep` hecho a mano cuyas `Options` no contengan la elegida, para comprobar que la regla de `Pick` = 0 no se cae. Pruébalo con un helper interno `DemoScript.IndexOr0(IReadOnlyList<string>, string)`: `Assert.Equal(0, DemoScript.IndexOr0(new[] { "A", "B" }, "Sin fondo"))`.

- [ ] **Step 2: Run** `dotnet test --filter GuideDemoTests`. Expected: no compila (faltan los tipos).
- [ ] **Step 3: Implement.** Los tramos de `At` (t módulo `Loop`):
  - 0–0,5: reposo.
  - 0,5–1,2: el cursor va de `Rest` a (0,0).
  - 1,2–1,4: `Pressed`.
  - 1,4–2,2: `Dragging` hacia `Wheel`.
  - 2,45–2,85: `KeyLit`.
  - Desde 2,6: `WheelOpen`.
  - 2,95–3,4: va a `PickPoint`.
  - 3,4–4,1: `Hot`.
  - En 4,1 suelta: se acaba `WheelOpen`, `Dragging`, `Pressed` y `Hot`.
  - Desde 4,3: `Result`.
  - 4,3–5,2: vuelve a `Rest`.

  Los movimientos usan ease-in-out cuadrático.
- [ ] **Step 4: Run** `dotnet test --filter GuideDemoTests`. Expected: PASS.

### Task 2: `GuideDemo` (el dibujo)

**Files:**
- Modify: `src/Mandarine/WheelWindow.xaml.cs`: `SegmentGeometry`, `Light`, `Accent`, `Logo`, `Palette` y las constantes de geometría pasan a `internal`. No cambia el comportamiento.
- Modify: `src/Mandarine/GuideDemo.cs` (agrega la clase)
- Test: `tests/Mandarine.Tests/GuideDemoTests.cs`

**Interfaces:**
- Consumes: `DemoStep`, `DemoTimeline` (Task 1); `WheelWindow.SegmentGeometry(double a0, double a1)` (internal static, en un lienzo de 210×210 centrado en 105).
- Produces: `public sealed class GuideDemo : Canvas`:
  - `void Attach(FrameworkElement photo, Border[] keys)`: la demo ajusta la opacidad de la foto real (0,35 mientras arrastra) y el estado "hundida" de la tecla del paso: borde inferior de 2 px, `TranslateTransform` Y=2, fondo #FFF1E8 y borde Accent.
  - `void Play(int step)`: arranca desde t=0 con `CompositionTarget.Rendering`. Si `SystemParameters.ClientAreaAnimation` es false, pinta `KeyFrame` y no se suscribe.
  - `void Stop()`: se desuscribe, oculta cursor, rueda, copia, archivo nuevo y aviso, y deja la foto y las teclas en reposo.
  - `bool Playing { get; }`.
  - `void ShowPose(DemoPose pose, DemoStep step)`: público para el test y la vista previa.

  Dibuja:
  - Un cursor flecha con relleno blanco y borde Ink de 1,4, que se encoge a 0,85 al presionar.
  - La copia de la foto (96×72, `DropShadowEffect` suave) pegada al cursor mientras arrastra.
  - Una rueda de 210 DIP con un segmento por opción, relleno `Light.Veil` sobre blanco y etiquetas Bold 11 (o `FontSize` reducido si no cabe, como `Fit`). Se abre con un `Spring` de escala (`DampingRatio = 0.6, Response = 0.35`, de 0,55 a 1) y opacidad. El segmento elegido se pinta con `Accent` y sale 5 DIP. Al centro va el logo, que se cambia por la etiqueta elegida mientras `Hot`.
  - El archivo nuevo a 120 DIP a la derecha de la foto, que entra con un resorte de escala de 0,4 a 1.
  - El aviso: un `Border` #202022 con franja Accent de 4 px y texto #F2F2F7, abajo a la derecha del escenario, a 14 DIP. Entra desde Y+80 con un resorte. Si hay `Output`, lleva "Abrir carpeta" en #FF6A24.

- [ ] **Step 1: Write the failing tests:**

```csharp
[Fact] public void Stop_leaves_nothing_moving() => OnSta(() =>
{
    var demo = new GuideDemo { Width = 860, Height = 480 };
    var photo = new Border(); var keys = new[] { new Border(), new Border(), new Border() };
    demo.Attach(photo, keys);
    demo.Play(0);
    demo.ShowPose(DemoTimeline.At(1.8, DemoScript.For(0)), DemoScript.For(0));
    Assert.Equal(0.35, photo.Opacity, 2);
    demo.Stop();
    Assert.False(demo.Playing);
    Assert.Equal(1, photo.Opacity);
});

[Fact] public void Renders_demo_keyframe() => OnSta(() => { /* ShowPose(KeyFrame(For(1))) on a 860×480 GuideDemo, render to %TEMP%\mandarine-guide-demo.png, Assert file > 5_000 bytes */ });
```

  El helper `OnSta` se copia de `GuideWindowTests`.
- [ ] **Step 2: Run** `dotnet test --filter GuideDemoTests`. Expected: no compila (`GuideDemo` no existe).
- [ ] **Step 3: Implement** según Interfaces.
- [ ] **Step 4: Run** `dotnet test --filter "GuideDemoTests|WheelPreviewTests"`. Expected: PASS. Mira `%TEMP%\mandarine-guide-demo.png` y compáralo con la opción B de `.superpowers/brainstorm/30812-1792983243/content/guia-animada.html`.

### Task 3: `GuideWindow` a lo ancho, con mirar → practicar

**Files:**
- Modify: `src/Mandarine/GuideWindow.xaml`, `src/Mandarine/GuideWindow.xaml.cs`
- Modify: `tests/Mandarine.Tests/GuideWindowTests.cs`

**Interfaces:**
- Consumes: `GuideDemo` (Task 2), `DemoScript.For` (Task 1); `Practice` sin cambios.
- Produces (además de lo que ya existe: `Finished`, `DoneCount`, `Current`, `TabText`, `IsDone`, `MarkDone`, `static Apply`):
  - `public int Shown { get; }`: el paso en pantalla. Al abrir vale `Current`, o 2 si `Current == 3`.
  - `public bool Practising { get; }` y `public bool Celebrating { get; }`.
  - `public string FooterText { get; }`.
  - `public GuideDemo Demo`, y los botones `SkipButton` / `NextButton` con `x:FieldModifier="public"`.
  - `public void Next()`: en el último paso dispara `Finished`; si no, `ShowStep(Shown + 1)`.
  - `public void BeginPractice()`: la llama el `PreviewMouseLeftButtonDown` de la foto. Hace `Demo.Stop()` y pone `Practising = true` y el pie "Ahora tú…".
  - `public void Advance()`: la llama el temporizador de 0,8 s después del ✓. Si quedan pasos sin hacer, va al siguiente sin hacer (`Current`). Si no, se queda en el último, con el pie "¡Listo! Ya sabes usar Mandarine" y la demo detenida.

  Reglas:
  - `ShowStep(int)` es privado: actualiza el título, las rayitas, las teclas y el pie, y llama a `Demo.Play`. El título entra con un resorte: opacidad 0→1 y Y 12→0, `Response = 0.3`.
  - `MarkDone(step)` con `step == Shown` pone `Celebrating`, muestra el ✓ (un círculo Accent de 28 px con "✓" Ink, que entra con un resorte sobre la esquina de la foto) e inicia el temporizador.
  - Cuando `DoDragDrop` vuelve sin celebración, un temporizador de 1,5 s pone `Practising = false` y vuelve a `Demo.Play(Shown)`. Si llega `MarkDone` en ese lapso, el temporizador se cancela.
  - `Closed` → `Demo.Stop()` y se detienen los temporizadores.

  Maqueta, de arriba hacia abajo:
  - Barra blanca de 64 DIP con línea inferior Line. Lleva el logo de 36 (`WheelWindow.Logo`), el título y el subtítulo, y tres `Border` de 28×4 con esquinas de 2 a la derecha: Accent si `i <= Shown` o el paso está hecho, Control si no.
  - Escenario Ground:
    - Arriba, a 22 DIP, el nombre del paso (SemiBold) y lo que hace (Muted), centrados.
    - Al centro, la foto (`Sample`, igual que hoy).
    - Abajo, a 22 DIP, las tres teclas: `Border` con borde Control, borde inferior de 4 px, radio 7, padding 14,9, ancho mínimo 64 y separación de 10. La del paso va en Ink y las otras en Muted.
    - `GuideDemo` encima de todo, con `IsHitTestVisible = false`.
  - Pie Manila con línea superior ManilaLine: el texto a la izquierda, y a la derecha Saltar y Siguiente/Listo (Siguiente con fondo Accent y texto Ink).

- [ ] **Step 1: Write the failing tests.** Se reemplaza `Steps_mark_in_any_order` por los nuevos tests más uno equivalente. `Skip_and_done_finish_but_close_does_not` pasa a usar `SkipButton`.

```csharp
[Fact] public void Opens_watching_the_first_undone_step() => OnSta(() =>
{ var g = new GuideWindow(); Assert.Equal((0, false, "Mira cómo se hace, o toma la foto para probar"), (g.Shown, g.Practising, g.FooterText)); g.Close(); });

[Fact] public void Pressing_the_photo_stops_the_demo() => OnSta(() =>
{ var g = new GuideWindow(); g.BeginPractice(); Assert.False(g.Demo.Playing);
  Assert.Equal("Ahora tú: arrastra la foto y, sin soltar, toca Shift", g.FooterText); g.Close(); });

[Fact] public void Done_step_celebrates_then_moves_on() => OnSta(() =>
{ var g = new GuideWindow(); g.MarkDone(1); Assert.Equal((0, false), (g.Shown, g.Celebrating));
  g.MarkDone(0); Assert.True(g.Celebrating); g.Advance(); Assert.Equal((2, false), (g.Shown, g.Practising));
  g.MarkDone(2); g.Advance(); Assert.Equal((2, "¡Listo! Ya sabes usar Mandarine", "Listo"), (g.Shown, g.FooterText, g.NextButton.Content)); g.Close(); });

[Fact] public void Next_walks_and_last_finishes_once() => OnSta(() =>
{ var g = new GuideWindow(); var finished = 0; g.Finished += () => finished++;
  g.Next(); g.Next(); Assert.Equal((2, "Listo", 0), (g.Shown, (string)g.NextButton.Content, finished));
  g.Next(); Assert.Equal(1, finished); g.Close(); });

[Fact] public void Closing_stops_the_demo() => OnSta(() =>
{ var g = new GuideWindow(); g.Show(); g.Close(); Assert.False(g.Demo.Playing); });
```

  `Apply_marks_only_practice`, `Apply_ignores_closed_guide` y `Renders_guide_preview` se quedan. `Renders_guide_preview` hace `g.Demo.ShowPose(KeyFrame(For(0)), For(0))` antes de renderizar.
- [ ] **Step 2: Run** `dotnet test --filter GuideWindowTests`. Expected: no compila (`Shown`, `BeginPractice`… no existen).
- [ ] **Step 3: Implement** según Interfaces. Se elimina `Pane`, `StepRow`, `_dots`, `_marks` y `Hint`.
- [ ] **Step 4: Run** primero `taskkill //IM Mandarine.exe //F` y después `dotnet test`. Expected: toda la suite PASS. Mira `%TEMP%\mandarine-guide.png`.
- [ ] **Step 5: Prueba manual.** Abre la app con `Start-Process '.\src\Mandarine\bin\Debug\net8.0-windows10.0.19041.0\Mandarine.exe'` y luego "Cómo se usa" en el menú del ícono. Comprueba esto:
  - La demo corre en bucle.
  - Al tomar la foto, la demo se detiene.
  - Shift → PNG real marca ✓ y pasa a Ctrl.
  - Soltar en el centro vuelve a la demo en 1,5 s.
  - Saltar y Listo cierran la guía.
