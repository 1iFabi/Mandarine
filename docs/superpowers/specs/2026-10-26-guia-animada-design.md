# "Cómo se usa" animada · diseño corto

Reemplaza la composición de `2026-10-26-como-se-usa-design.md`. La lógica de práctica de ese diseño se queda igual: la foto de ejemplo, los pasos que se marcan solos, `Apply`, `Finished`, el registro y el menú.

## Por qué

La guía actual es una foto quieta a la izquierda y una lista de texto a la derecha. Está descentrada y no muestra el gesto: quien la abre no ve cómo se arrastra, qué tecla tocar ni qué hace la rueda. Hay que enseñar el movimiento y no describirlo.

## Qué se ve (opción B de la maqueta `guia-animada.html`)

Ventana de 860×600, sin panel lateral. Va de arriba hacia abajo:

- **Barra superior blanca:** logo de 36 px, "Cómo se usa Mandarine" en SemiBold con "Tres gestos, uno a la vez" debajo en Muted, y a la derecha tres rayitas de progreso de 28×4 px (naranja si el paso está hecho o es el actual, #D6D6D6 si no). Abajo lleva una línea #E5E5E5.
- **Escenario** (fondo Ground #F3F3F3, ocupa el resto):
  - Arriba y al centro, el título del paso ("Convertir · Shift") en SemiBold y lo que hace ("Apunta a un formato y suelta") en Muted.
  - Al centro, la foto `mandarina.jpg` de 96×72 con su nombre.
  - Abajo y al centro, tres teclas (Shift, Ctrl, Alt) dibujadas como teclas físicas: borde inferior de 4 px. La del paso actual va en Ink y las otras en Muted.
- **Pie manila:** a la izquierda la instrucción del momento y a la derecha Saltar y Siguiente (o Listo en el último paso).

Se usa un solo tamaño de texto, 13, sin gradientes ni sombras decorativas, como dice DESIGN.md. La única sombra es la de la foto mientras se arrastra, porque flota.

## La demostración (una por paso, se repite)

La dibuja el código en la misma ventana, con los resortes de `Spring`/`SpringClock` que ya usa la rueda. Dura unos 6 s por vuelta:

1. Un cursor dibujado va hasta la foto y la presiona: el cursor se encoge un poco y la foto original queda al 35 %.
2. Arrastra una copia de la foto hacia la derecha del centro.
3. La tecla del paso se hunde y se tiñe de naranja claro.
4. La rueda se abre ahí con un resorte con rebote (de 0,55 a 1 de escala). Usa la misma geometría de segmentos y la misma paleta que la rueda real. En el centro está el logo.
5. El cursor apunta a una opción. El segmento se pone naranja, sale 5 px hacia afuera y su nombre aparece en el centro.
6. Suelta: la rueda se va y aparece el resultado.

Opciones y resultados de cada paso:

| Paso | Tecla | Opciones de la rueda | Elige | Resultado |
|---|---|---|---|---|
| Convertir | Shift | las de `Catalog.TargetsFor` para un .jpg | PNG | aparece `mandarina.png` al lado, con un pop, y un aviso dibujado abajo a la derecha: "mandarina.png en Práctica" |
| Herramientas | Ctrl | las de `Catalog.ToolsFor` | Sin fondo | aparece `mandarina-sin-fondo.png` y su aviso |
| Acciones | Alt | las de `Catalog.ActionsFor` | Copiar | sin archivo nuevo; el aviso dice "Copiado: pégalo donde quieras" |

Las opciones salen del catálogo real, así que la demo cambia sola si cambia la rueda. Si son más de 8, se muestran las 8 primeras.

El aviso de la demo está dibujado dentro del escenario con el estilo de la notificación real: línea oscura con franja naranja de 4 px. No es la `NoticeWindow` real.

## Mirar y practicar

- La demo se repite hasta que la persona presiona la foto. En ese momento la demo se detiene al instante y se limpia, y la foto real queda en su lugar, lista para arrastrarse (el `DoDragDrop` de hoy).
- El pie dice entonces "Ahora tú: arrastra la foto y, sin soltar, toca **Shift**".
- Si el gesto real termina bien (`MarkDone` llega por `Apply`), aparece un ✓ naranja sobre la foto. Después de unos 0,8 s pasa al siguiente paso, cuyo título entra con un resorte, y la demo vuelve a empezar.
- Si suelta sin lograrlo, la demo vuelve a correr después de 1,5 s.
- **Siguiente** pasa al paso siguiente sin marcarlo. En el último paso dice **Listo** y dispara `Finished`, igual que hoy. **Saltar** también dispara `Finished`.
- El paso que se muestra es el primero sin hacer (`Current`). Siguiente puede adelantarse a él.
- Si Windows tiene las animaciones apagadas (`SpringClock.ReduceMotion`), no hay demo en movimiento: se muestra el momento clave quieto (la rueda abierta con la opción elegida y la tecla iluminada).

## Lo que se mantiene

`Practice`, `Apply`, `MarkDone`, `IsDone`, `DoneCount`, `Current`, `TabText`, `Finished`, la instancia única y la apertura al inicio. `TabText` sigue existiendo para los tests, aunque ya no se muestre en una pestaña.

## Lo que se va

El `EditorPane` dentro de la guía y la lista de pasos con puntos. `EditorPane.TabLabel` se queda porque es pequeño y genérico.

## Enfoques considerados

1. **Dibujarla con código y resortes (elegido).** Se ve nítida en cualquier escalado, sigue al catálogo y no agrega archivos.
2. Un video o GIF grabado: hay que regrabarlo con cada cambio, pesa más y se ve borroso al escalar.
3. Mover la rueda real con un cursor falso: es una ventana aparte que escucha el mouse y el teclado reales, y chocaría con la práctica.

## Queda al criterio de quien lo construye

Los tiempos exactos de cada tramo, la forma del cursor (una flecha con borde Ink) y si la geometría del segmento se comparte haciendo `internal` el método de `WheelWindow` o moviéndolo a un helper.
