# Notificación al terminar · diseño corto

**Para qué.** Hoy, al terminar, Mandarine muestra el globo genérico de Windows ("Listo · MKV: 1 archivo(s)"): no dice dónde quedó el archivo ni se parece a Mandarine. Se reemplaza por una notificación propia que dice qué se hizo y dónde, con un atajo a la carpeta.

**Objetivo.** Cada conversión, herramienta o acción termina con una línea que nombra el resultado y su carpeta. **No es objetivo:** historial de notificaciones, sonidos, el centro de notificaciones de Windows. **Anti-objetivo:** que robe el foco o tape el trabajo.

**Diseño elegido (opción C de las maquetas).** Una sola línea oscura (#1C1C1E, texto #F2F2F7, secundario #A1A1A6) con una franja de 4 px a la izquierda, esquinas de 8 px y una sombra, abajo a la derecha de la pantalla de trabajo, en Segoe UI Variable Text 13:
`✓ reunion-lunes.mkv  en Videos   Abrir carpeta` (enlace en #FF6A24).

- **Éxito:** franja naranja #FF5200. Un archivo: su nombre. Varios: "3 archivos".
- **Error:** franja roja y "✗ No se pudo convertir <archivo>" o "✗ Fallaron 2 de 5". El detalle va en el texto secundario, recortado.
- **Abrir carpeta:** abre el Explorador con el archivo seleccionado (o la carpeta, si son varios).
- **Duración:** se va sola a los 5 s. Con el mouse encima se queda; un clic en la línea la cierra.
- **Foco:** ventana propia de WPF, siempre encima, sin activarse y fuera de la barra de tareas, como la rueda (`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`).

**Enfoques.** (1) Ventana propia de WPF: idéntica a la maqueta. **Elegido.** (2) Notificaciones nativas de Windows: Windows impone su diseño y las guarda en el centro de notificaciones. Descartado porque no puede verse como la C.

**Qué se toca.**
- Ventana nueva de notificación.
- `App.Notify` y sus 5 llamadas en `App.xaml.cs` pasan a la nueva ventana. El aviso "Mandarine está listo" se queda igual hasta que llegue el "Cómo se usa".
- Los conversores ya devuelven la ruta de salida (`Converter.ConvertAsync` y `CombineAsync` devuelven `Task<string>`).
- `Editor.Run` devuelve hoy solo cuántos archivos guardó: pasa a devolver las rutas.
- Las acciones (copiar, mover…) solo notifican si fallan, como ahora.

**Decisiones de Claude (revisables).** El texto para varios archivos y para errores, los 5 s, el clic para cerrar y que el editor devuelva rutas.
