# "Cómo se usa" con práctica · diseño corto

**Para qué.** Quien abre Mandarine por primera vez no sabe que hay que tocar una tecla **mientras arrastra**. Hoy solo lo dice un globo de Windows ("Arrastra un archivo y, sin soltarlo, toca Shift"). Se reemplaza por una ventana que enseña haciendo: se practica con una foto de ejemplo y se usan la rueda y la notificación de verdad.

**Objetivo.** Al terminarla, la persona hizo una vez cada gesto: convertir (Shift), herramientas (Ctrl) y acciones (Alt). **No es objetivo:** un recorrido por todos los formatos, videos o animaciones explicativas. **Anti-objetivo:** tocar archivos de la persona, o aparecer cada vez que se abre la app.

**Diseño (maqueta aprobada).** Ventana con el estilo de los editores (`DESIGN.md`):
- A la izquierda, la zona de práctica (fondo Ground #F3F3F3) con la foto de ejemplo `mandarina.jpg`, que se arrastra como un archivo real. Debajo, la pista del paso actual ("Arrastra la foto y, sin soltar, toca **Shift**").
- A la derecha, el panel blanco de 320 px: logo, "Cómo se usa Mandarine", "Practica con esta foto: no toca tus archivos", y 3 pasos (Convertir · Shift, Herramientas · Ctrl, Acciones · Alt). Cada paso tiene un punto: gris si falta, con borde naranja si es el actual, naranja con ✓ si está hecho. Más abajo, una nota: soltar en el centro o **Esc** cancela, y lo nuevo queda al lado del original.
- Al pie, la pestaña manila con "n de 3 listo", **Saltar** y **Listo**.

**Cuándo cuenta un paso (opción A, elegida).** Cuando se completa un gesto entero desde esa rueda con la foto de práctica: soltar sobre una opción que termina bien. Un paso se puede hacer en cualquier orden, y el resaltado pasa al primero que falte.

**Enfoques.** (1) Ventana WPF propia que reutiliza el panel de los editores: es la única forma en que se puede practicar con la rueda real. **Elegido.** (2) Una página o imágenes estáticas: no permite practicar. Descartado.

**Comportamiento.**
- **Carpeta de práctica:** `%TEMP%\Mandarine\Práctica`. La foto se crea ahí al abrir la ventana, y se vuelve a crear si desaparece (por ejemplo, después de "Mover") apenas la ventana lo note.
- **Primera vez:** se abre sola al iniciar si en `HKCU\Software\Mandarine` no existe `GuideShown`. **Listo** o **Saltar** escriben `GuideShown = 1`. Cerrar con la X sin terminar no lo escribe, así que vuelve a abrirse la próxima vez.
- **Menú del ícono:** una entrada nueva "Cómo se usa", arriba de "Rueda oscura", la abre en cualquier momento.
- **El globo de Windows desaparece:** el aviso "Mandarine está listo" se quita y con él `Notify` y el globo.
- **Solo cuenta la práctica:** lo que la persona haga con sus propios archivos mientras la ventana está abierta no marca pasos.

**Decisiones de Claude (revisables).** La carpeta en `%TEMP%`, que la X no cuente como visto, el orden libre de los pasos y que la foto se dibuje con el logo de Mandarine sobre un degradado naranja.

**Riesgo conocido.** El arrastre sale de la propia ventana de Mandarine, cuyo hilo de interfaz queda dentro del bucle de arrastre de Windows mientras dura. La rueda se abre por mensajes que ese bucle procesa, pero hay que comprobarlo temprano con un arrastre real.
