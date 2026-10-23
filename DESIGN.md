# Mandarine · diseño de los editores

**Idea: la carpeta de archivo.** El trabajo ocupa la izquierda; a la derecha, un panel blanco de 320 px con las opciones, y al pie una pestaña manila que dice qué archivo se va a escribir, dónde y con qué tamaño, antes de guardar. Es una app de Windows para trabajo de oficina: los controles nativos y las convenciones de Windows (Enter guarda, Esc cancela, Ctrl+rueda amplía) se mantienen.

Código: `src/Mandarine/EditorStyles.xaml` (tokens y estilos) y `EditorPane.xaml` (el panel). Lo usan `EditorWindow` y `PdfPagesWindow`.

## Color

| Token | Valor | Uso |
|---|---|---|
| Accent | #FF5200 | Botón Guardar, selección, cortes, relleno de sliders. Nunca para texto sobre blanco. |
| AccentHover / AccentPressed | #FF6A24 / #E84A00 | Estados del acento. |
| Ink | #1C1C1E | Texto principal; también el texto del botón Guardar (5,2:1 sobre el acento; el blanco daba 3,25:1). |
| Muted | #5E5E63 | Rótulos y notas (Caption, GroupLabel). |
| Ground | #F3F3F3 | Fondo de la zona de trabajo. |
| Line / Control | #E5E5E5 / #D6D6D6 | Bordes de panel y de controles. |
| Manila / ManilaInk / ManilaLine | #E7D3A8 / #5A4520 / #CBB78C | Sólo la pestaña "Se guardará". |
| Lector | #1C1C1E | Fondo opaco del lector de páginas. |

## Tipografía

Segoe UI Variable Text (o Segoe UI), **un solo tamaño: 13**. La jerarquía va por peso (SemiBold para el nombre de la herramienta, el archivo de salida y los nombres de las partes) y por color (Ink / Muted). Las cifras (píxeles, porcentajes, páginas) son tabulares.

## Panel

- Cabecera: logo de 36 px, herramienta, archivo y "n de m" cuando hay varios archivos.
- Cuerpo: grupos apilados (`EditorPane.Add`), rótulo arriba y 20 px entre grupos; se desplaza si no cabe.
- Segmentados: hasta 4 opciones en una fila; con más, 3 columnas (no se truncan).
- Filas de botones: anchos iguales (UniformGrid).
- Sliders: nombre a la izquierda y valor a la derecha, encima del control; doble clic vuelve a cero.
- Pestaña: nombre del archivo nuevo, detalle ("1200 × 800 px · junto al original, en Fotos"), Cancelar y Guardar.

## No hacer

Gradientes, sombras decorativas, iconos en cuadrados de color, más tamaños de texto, texto blanco sobre el acento.
