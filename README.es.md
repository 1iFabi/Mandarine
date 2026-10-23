<p align="center"><img src="docs/images/banner.jpg" alt="Mandarine" width="640"></p>

# Mandarine

[English](README.md) · **Español**

Arrastra un archivo, toca **Shift**, **Ctrl** o **Alt** y elige en una rueda. Convierte, comprime, recorta, anota y
comparte imágenes, audio, video, PDF y archivos de Office, sin conexión, en Windows.

<table align="center">
  <tr>
    <th align="center"><kbd>Shift</kbd> · Convertir</th>
    <th align="center"><kbd>Ctrl</kbd> · Herramientas</th>
    <th align="center"><kbd>Alt</kbd> · Acciones</th>
  </tr>
  <tr>
    <td><img src="docs/images/wheel-convert.png" width="210" alt="La rueda de conversión sobre un video, apuntando a MKV"></td>
    <td><img src="docs/images/wheel-tools.png" width="210" alt="La rueda de herramientas sobre una foto, apuntando a Anotar"></td>
    <td><img src="docs/images/wheel-actions.png" width="210" alt="La rueda de acciones sobre una foto, apuntando a Copiar"></td>
  </tr>
</table>

## Descarga

1. Entra en [Releases](https://github.com/1iFabi/Mandarine/releases) y descarga `Mandarine-vX.Y.Z-win-x64.zip`.
2. Descomprímelo donde quieras (por ejemplo `C:\Programas\Mandarine`) y abre `Mandarine.exe`.
3. Aparece una mandarina en el área de notificación. Listo: Mandarine queda esperando en segundo plano.

Requisitos: Windows 10 (versión 2004 o posterior) o Windows 11, de 64 bits. No hay que instalar nada más: .NET y
todos los conversores vienen dentro del zip.

> **¿"Windows protegió su PC"?** La app todavía no está firmada, así que SmartScreen avisa la primera vez.
> Haz clic en **Más información → Ejecutar de todas formas**. Puedes comprobar la descarga con el SHA-256 que figura
> en cada release.

Para que arranque con Windows, haz clic derecho en el ícono de la bandeja y marca **Iniciar con Windows**.

## Cómo se usa

1. Empieza a arrastrar uno o varios archivos (desde el Explorador, el escritorio, las descargas del navegador...).
2. **Sin soltar el mouse**, toca una tecla:

   | Tecla | Rueda | Qué tiene |
   |---|---|---|
   | **Shift** | Convertir | Los formatos a los que se pueden pasar los archivos |
   | **Ctrl** | Herramientas | Comprimir, recortar, rotar, quitar el fondo, OCR... |
   | **Alt** | Acciones | Compartir, copiar, mover, copiar la ruta, terminal, mostrar en la carpeta |

3. Apunta a una opción (el centro muestra su nombre) y suelta.

El resultado se guarda **al lado del original**, nunca encima (`foto.jpg` → `foto.webp`, o `foto (1).jpg` si el nombre
ya existe). Para cancelar, suelta sobre el centro o pulsa **Esc**. Si tocas otra tecla durante el arrastre, cambia la rueda.

Con clic derecho en el ícono de la bandeja están **Rueda oscura**, **Iniciar con Windows** y **Salir**.

## Qué puede hacer

### Convertir (Shift)

| Desde | A |
|---|---|
| Imágenes: JPG, PNG, WebP, HEIC, TIFF, AVIF, BMP | JPG, PNG, WebP, TIFF, AVIF, BMP, PDF (varias imágenes → un solo PDF) |
| Audio: MP3, M4A, AAC, WAV, FLAC, OGG, Opus, AIFF, WMA | MP3, M4A, WAV, FLAC, OGG, Opus, AIFF, WMA |
| Video: MP4, M4V, MOV, MKV, WebM, AVI, WMV, GIF | MP4, MOV, MKV, WebM, AVI, WMV, GIF |
| Documentos: DOCX, DOC, ODT, RTF, HTML, Markdown, TeX, TXT | PDF, DOCX, ODT, RTF, MD, HTML, TXT |
| Planillas: XLSX, XLS, ODS, CSV, TSV | PDF (columnas ajustadas al contenido), XLSX, CSV, TSV, HTML, TXT (tabla alineada), MD (tabla Markdown); una sección por hoja |
| Presentaciones: PPTX, PPSX, POTX | PDF, PNG, JPG, GIF, MD, TXT |
| PDF | DOCX, PPTX (una diapositiva por página), PNG / JPG (una imagen por página), TXT, HTML, RTF |

### Herramientas (Ctrl)

| Para | Herramientas |
|---|---|
| Imágenes | **Recortar**, **Anotar** (flecha, rectángulo, lápiz, resaltador, texto), **Ocultar** (pixelar o tapar en negro), **Ajustes** (brillo, contraste, saturación, calidez), **Fondo** (degradado o color, margen, esquinas redondeadas, sombra, proporción), **Redimensionar** (por porcentaje o en píxeles exactos, sin deformar), **Sin fondo**, **Rotar** (90°, 180° o 270°), Comprimir, Metadatos |
| Audio | Normalizar el volumen, Metadatos |
| Video | Comprimir, **Rotar** (90°, 180° o 270°), Metadatos |
| PDF | **Rotar** (todas las páginas o las que elijas), **Dividir** (por rangos de páginas), OCR (vuelve buscables los escaneos; español e inglés), Comprimir, Metadatos, Unir (2 o más PDF) |
| Cualquier cosa | Crear un ZIP (archivos y carpetas); sobre un `.zip`, Extraer |

Los editores de imagen abren una ventana: la imagen a la izquierda, las opciones a la derecha y, debajo, una pestaña
que dice qué archivo se va a escribir, con qué tamaño y en qué carpeta. **Recortar** (arrastrando o escribiendo el área
en píxeles), **Ajustes**, **Fondo** y **Redimensionar** se configuran una vez y se aplican a todas las imágenes
arrastradas; **Anotar** y **Ocultar** van de a una. Enter guarda, Esc cancela y Ctrl+Z deshace.

**Rotar** también abre una ventana para elegir el ángulo. En un PDF muestra las páginas: haz clic en las que quieras
girar (o en ninguna, para girarlas todas). **Dividir** muestra las páginas y, a la derecha, las partes como rangos
("desde la 3 hasta la 7"): escríbelos, corta haciendo clic entre dos páginas o usa **Cada página** y **Por la mitad**.
Un solo rango extrae esas páginas a un PDF nuevo; varios se guardan en una carpeta junto al original. El tamaño de las
páginas se cambia a la derecha (o con Ctrl + rueda) y un doble clic muestra una página en grande.

### Acciones (Alt)

**Compartir** (la hoja de compartir de Windows: dispositivos cercanos, correo...), **Copiar** (para pegar en el
Explorador), **Mover** a una carpeta, copiar la **Ruta**, abrir una **Terminal** ahí y **Mostrar** en el Explorador.

### Privacidad

Todo se procesa en tu PC. Ningún archivo, nombre ni dato de uso sale de ella: Mandarine no tiene código de red.

## Cómo funciona

```
 tocas Shift/Ctrl/Alt          aparece la rueda               sueltas sobre una opción
 con el mouse apretado   ──►   bajo el cursor           ──►   Mandarine hace el trabajo
 (hook de teclado)             (le pregunta a Catalog)        (Converter → motor)
```

1. **El gesto.** Un hook de teclado de bajo nivel (`KeyboardHook.cs`) detecta una tecla modificadora tocada sola
   mientras el botón izquierdo está apretado, algo que solo pasa durante un arrastre.
2. **La rueda.** `WheelWindow` captura la pantalla bajo el cursor, la desenfoca como vidrio esmerilado (`Backdrop.cs`)
   y muestra el anillo de opciones sin robarle el foco a la app desde la que arrastras. Las animaciones usan resortes
   (`Spring.cs`) y respetan el ajuste "Efectos de animación" de Windows.
3. **Las opciones.** `Catalog.cs` relaciona cada extensión con lo que se puede hacer con ella: solo ofrece lo que vale
   para *todos* los archivos arrastrados, y solo si la herramienta que lo hace está disponible.
4. **El trabajo.** `Converter.cs` ejecuta la opción con el motor adecuado y escribe el resultado junto al original:

   | Motor | Para qué |
   |---|---|
   | [FFmpeg](https://ffmpeg.org) | Imágenes, audio, video, rotar, comprimir, normalizar |
   | [wordcraft](https://github.com/storytold/wordcraft), [gridcraft](https://github.com/storytold/gridcraft), [deckcraft](https://github.com/storytold/deckcraft) | Documentos, planillas, presentaciones |
   | [pdfcraft](https://github.com/storytold/pdfcraft) | Todo lo de PDF salvo PDF → DOCX |
   | Microsoft Word (si está instalado) | PDF → DOCX, con mejor diseño que la alternativa de pdfcraft |
   | [Tesseract](https://github.com/tesseract-ocr/tesseract) (`Ocr.cs`) | OCR: lee cada página y le pone encima una capa de texto invisible |
   | [BiRefNet](https://github.com/ZhengPeng7/BiRefNet) sobre [ONNX Runtime](https://onnxruntime.ai) (`Background.cs`) | Quitar el fondo |
   | Propio (`ImageEdit.cs`, `EditorWindow`, `Metadata.cs`, `Archive.cs`, `Actions.cs`, `Share.cs`) | Editores de imagen, limpieza de metadatos JPEG/PNG sin pérdida, ZIP, acciones, compartir |

   Las herramientas externas son programas aparte que Mandarine ejecuta; viven en `tools\`, junto a la app.

| Ruta | Qué hay |
|---|---|
| `src/Mandarine/` | La app (C#, .NET 8, WPF) |
| `tests/Mandarine.Tests/` | Tests xUnit: cada conversión y herramienta sobre archivos reales, más vistas previas de la rueda y los editores |
| `scripts/fetch-tools.ps1` | Descarga las herramientas en versiones fijas y verifica el SHA-256 de cada una |
| `scripts/package.ps1` | Arma el zip de la release |

## Compilar desde el código

Necesitas el [SDK de .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) y Windows PowerShell.

```powershell
git clone https://github.com/1iFabi/Mandarine.git
cd Mandarine
.\scripts\fetch-tools.ps1          # ~580 MB de conversores, modelos y datos de OCR, en src\Mandarine\tools\
dotnet test tests\Mandarine.Tests  # opcional: convierte archivos reales con cada motor (~30 s)
dotnet run --project src\Mandarine
```

### Crear una release

```powershell
.\scripts\package.ps1 -Version 1.0.1
```

Descarga las herramientas, corre los tests, compila una versión autocontenida, comprueba que estén todas las
herramientas y genera `dist\Mandarine-v1.0.1-win-x64.zip` con su SHA-256. Después se publica, por ejemplo con GitHub CLI:

```powershell
gh release create v1.0.1 dist\Mandarine-v1.0.1-win-x64.zip --title "Mandarine 1.0.1" --notes "SHA-256: <hash>"
```

## Limitaciones conocidas

- Todavía no se puede recortar ni ocultar partes de video o audio.
- Alt puede activar la barra de menú de la ventana que queda bajo el cursor después de soltar.
- El OCR lee español e inglés, no corrige páginas torcidas y puede perder letras poco comunes como la "ü".
- Quitar el fondo tarda unos 10 segundos por imagen en la CPU.
- Las imágenes editadas pierden sus metadatos (EXIF).

## Créditos y licencia

Mandarine se publica bajo la [licencia MIT](LICENSE). Las herramientas incluidas mantienen sus propias licencias,
detalladas en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md); en particular, el FFmpeg incluido es GPLv3.

Inspirado en el flujo de trabajo de Tangerine para Mac. Mandarine es un proyecto independiente, con código, nombre e
imagen propios, sin relación con Tangerine ni con sus autores.

Foto de la portada de [Pexels](https://www.pexels.com/photo/30552332/).
