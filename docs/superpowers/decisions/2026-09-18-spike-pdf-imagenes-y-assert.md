# Spike: imágenes PDF en Linux y técnica de assert de contenido

Fecha: 2026-09-18
Contexto: Tarea 0 de docs/superpowers/plans/2026-09-18-export-pdf-tablas.md

## (a) ¿MigraDoc embebe imágenes PNG en Linux?

Sí, con una salvedad no anticipada por el plan (ver "Gotcha no anticipado" abajo).

`PDFsharp-MigraDoc` 6.2.4 (paquete base, no las variantes `-GDI`/`-WPF`) no depende de
`System.Drawing`/GDI+. Verificado empíricamente corriendo `spike-pdf/Program.cs` en este
entorno (Linux/WSL): un PNG de 1x1 embebido vía el pseudo-protocolo `"base64:" +
Convert.ToBase64String(bytes)` (pasado directo a `Cells[i].AddImage(...)`, sin tocar el
filesystem) se renderiza y PdfPig lo detecta al releer el PDF con `page.GetImages()`
(`Cantidad de imágenes detectadas por PdfPig: 1`).

Decisión: el membrete (Tarea 6) embebe el logo como recurso `EmbeddedResource` dentro de
`StockApp.Documentos`, leído a `byte[]` y pasado a `AddImage` con el prefijo `"base64:"`. Sin
archivos temporales, sin `System.Drawing`.

### Gotcha no anticipado: resolución de fuentes es OBLIGATORIA en TODAS las plataformas

El plan asumía (basado en la descripción de NuGet) que el único riesgo en Linux era la
imagen. Al correr el spike con solo la imagen y dos párrafos de texto, MigraDoc explotó ANTES
de llegar a la imagen:

```
Unhandled exception. System.InvalidOperationException: The font 'Courier New' cannot be
resolved for predefined error font. Use another font name or fix your font resolver.
 ---> System.InvalidOperationException: No appropriate font found for family name
'Courier New'. Implement IFontResolver and assign to 'GlobalFontSettings.FontResolver'
to use fonts.
   at PdfSharp.Drawing.XGlyphTypeface.GetOrCreateFrom(...)
   ...
   at MigraDoc.Rendering.PdfDocumentRenderer.RenderDocument()
```

Esto NO es un problema exclusivo de Linux: el paquete `PDFsharp-MigraDoc` (a diferencia de
`PDFsharp-GDI`, que sí usa `System.Drawing`/fuentes de Windows vía GDI+) nunca lee fuentes del
sistema operativo en ninguna plataforma. Sin un `IFontResolver` asignado a
`PdfSharp.Fonts.GlobalFontSettings.FontResolver`, CUALQUIER render de texto (no solo con
imagen) falla, en Windows o en Linux. Esto afecta a TODAS las tareas del plan que rendericen
texto — es decir, prácticamente todas (1, 3, 4, 5, 6, 7...), no solo la del membrete.

Se implementó un `IFontResolver` mínimo para el spike que mapea cualquier familia pedida a
`Liberation Sans` (Regular/Bold), leída de
`/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf` (ya presente en este
entorno WSL vía el paquete `fonts-liberation`). Con eso, el render de texto + imagen + tabla
funcionó de punta a punta y PdfPig confirmó el contenido.

```csharp
GlobalFontSettings.FontResolver = new SpikeFontResolver();

sealed class SpikeFontResolver : IFontResolver
{
    private static readonly Dictionary<string, string> RutasPorFaceName = new()
    {
        ["LiberationSans-Regular"] = "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        ["LiberationSans-Bold"] = "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
    };

    public byte[] GetFont(string faceName) => File.ReadAllBytes(RutasPorFaceName[faceName]);

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? "LiberationSans-Bold" : "LiberationSans-Regular");
}
```

**Decisión mínima para desbloquear la Tarea 1 (scaffolding):** `StockApp.Documentos` necesita
un `IFontResolver` propio asignado una sola vez (constructor estático o inicialización del
exportador) que resuelva las fuentes desde bytes embebidos como `EmbeddedResource` del propio
proyecto — NO desde rutas del sistema operativo (`/usr/share/fonts/...` no existe en el
Windows de producción, y ni siquiera está garantizado en todos los Linux/WSL). Fuente
candidata: **Liberation Sans** (licencia SIL Open Font License 1.1, redistribuible, métricamente
compatible con Arial) — mismo criterio que se usó para el spike, pero con los archivos `.ttf`
embebidos en el proyecto en vez de leídos del filesystem del SO.

**Esto queda marcado como pendiente concreto de la Tarea 1, no resuelto acá**: la Tarea 0 solo
prueba que la técnica (imagen + `IFontResolver` con fuente embebida) FUNCIONA; la
implementación real del resolver de producción (qué clase, qué namespace, cómo se embeben los
`.ttf`, si hace falta Bold/Italic para el membrete) es tarea de quien implemente la Tarea 1 y
debe revisarse contra el diseño del plan antes de escribir código, porque el plan actual no la
menciona en ningún paso.

## (b) ¿Cómo se asserta el contenido de un PDF generado?

Con `UglyToad.PdfPig` (MIT, gestionado, cross-platform), SOLO como dependencia de test —
nunca de producción. `PdfDocument.Open(byte[])` devuelve un documento navegable. Confirmado
empíricamente en el spike:

### Lo que SÍ se puede afirmar (con ejemplo real que corrió en verde)

- **Presencia de texto** (título, columnas, valores) — concatenando el texto de todas las
  páginas y buscando substrings:
  ```csharp
  using var doc = PdfDocument.Open(bytes);
  var texto = string.Join(" ", doc.GetPages().Select(p => p.Text));
  Assert.Contains("SPIKE-TITULO-DE-PRUEBA", texto);
  ```
- **Cantidad de páginas**:
  ```csharp
  Assert.Equal(2, doc.NumberOfPages);
  ```
- **Orientación de página** (comparando ancho vs. alto en points; A4 apaisado dio
  `841.89 x 595.276`, A4 vertical es al revés):
  ```csharp
  var pagina = doc.GetPage(1);
  Assert.True(pagina.Width > pagina.Height); // apaisado
  ```
- **Cantidad de imágenes embebidas**:
  ```csharp
  var cantidadImagenes = doc.GetPages().Sum(p => p.GetImages().Count());
  Assert.True(cantidadImagenes >= 1);
  ```
- **Que un salto de página ocurrió de verdad** (contenido de la página 2 presente y
  distinguible de la 1) — se confirmó con `AddPageBreak()` + búsqueda del texto de la segunda
  página.
- **Que un texto largo NO fue truncado**: comparando longitud de caracteres extraídos vs.
  longitud del texto original, y verificando que el primer Y el último fragmento del texto
  están presentes. En el spike, un texto de 401 caracteres apareció íntegro (411 caracteres
  extraídos en la página, incluyendo el texto anterior de la misma página) con el primer
  fragmento (`palabra1`) y el último (`palabra40`) ambos presentes.

### Lo que NO se puede afirmar (o no se probó que se pueda)

- **Coincidencia carácter-por-carácter con separadores/espaciado exactos del texto original**:
  PdfPig extrae texto por posición de glifo en la página, no por el string original que se le
  pasó a MigraDoc. Un `Contains` exacto del string original con guiones (`"TEXTOLARGO-palabra1-..."`)
  puede fallar aunque el contenido esté completo, porque el layout puede introducir o perder
  espacios en los límites de línea. **Conclusión práctica: comparar por longitud +
  fragmentos clave (inicio/fin/valores), no por igualdad exacta de string largo.**
  Combinado con el punto anterior, sirve para el requisito real de negocio: encontrar SPIKE-VALOR-123 (por ejemplo) no requiere una comparación estricta.
- **A qué columna de una tabla pertenece un texto**: PdfPig da texto por página (o por
  `Letter`/posición X,Y si se necesita más precisión), no un modelo de "celda de tabla" como
  MigraDoc. No se necesitó ni se probó extracción por columna en este spike; si una tarea
  futura necesita afirmar "la columna X tiene el valor Y" en una tabla con varias columnas de
  texto, hay que usar la posición (`Letter.BoundingBox` — ver corrección más abajo) — no
  probado acá, queda fuera de alcance de la Tarea 0.
- **Estilos visuales** (fuente usada, negrita, color, tamaño de punto) no se intentó leer ni
  afirmar — PdfPig expone esta información (`Letter.Font`, etc.) pero no se verificó
  empíricamente en este spike porque ninguna tarea del plan lo requiere.

### Correcciones post-spike (halladas en la Tarea 6, no en la Tarea 0)

Estas dos correcciones actualizan afirmaciones de este documento que resultaron incompletas o
desactualizadas al implementar contenido real (membrete con tabla de layout). Las Tareas 1-20
deben leer esto ANTES de asumir vigente el texto de arriba en estos dos puntos puntuales:

1. **`Page.Text` concatena palabras SIN espacios cuando el contenido viene de una tabla sin
   ancho de columna fijo** (el caso del membrete de la Tarea 6, y de cualquier tabla de layout
   similar). El spike de la Tarea 0 nunca lo notó porque todos sus asserts comparaban tokens de
   una sola palabra (p. ej. `SPIKE-VALOR-123`, `palabra030`), donde la ausencia de espacios
   alrededor es invisible. Para afirmar sobre una FRASE de varias palabras (p. ej.
   `"INTENDENCIA DE CARMELO"`, un título, una descripción de filtros) en contenido de este tipo,
   `Assert.Contains` sobre `string.Join(" ", doc.GetPages().Select(p => p.Text))` puede fallar
   aunque el contenido esté completo. La técnica correcta es reconstruir el texto desde
   `Page.GetWords()` (que sí segmenta palabras correctamente) uniendo con espacio explícito:
   ```csharp
   var texto = string.Join(" ", doc.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
   ```
   Para tokens sueltos sin espacios internos (`palabra030`, `Codigo`, un código de producto),
   `Page.Text` sigue sirviendo sin cambios — esto NO invalida el punto 3 de "LA DECISIÓN" para
   ese caso, solo lo acota cuando el contenido puede venir de una tabla de layout sin ancho fijo
   y la aserción es sobre una frase de varias palabras.

2. **`Letter.GlyphRectangle` está OBSOLETA** en la versión de `PdfPig` que usa este proyecto
   (warning `CS0618` al compilar código que la referencia) — este documento la mencionaba como
   vigente en dos lugares. El reemplazo, con la misma semántica de rectángulo de posición, es
   **`Letter.BoundingBox`** (y, de forma equivalente, `Word.BoundingBox` para palabras completas
   reconstruidas con `GetWords()`). Verificado empíricamente en la Tarea 6 comparando
   `Word.BoundingBox.Top` de una palabra puntual entre dos variantes del mismo documento. Todas
   las referencias a `GlyphRectangle` en este documento deben leerse como `BoundingBox`.

## LA DECISIÓN (vinculante para las Tareas 1 a 20)

Todos los tests de `StockApp.Documentos.Tests` usan **PdfPig** para afirmar sobre contenido
generado, con esta técnica concreta:

1. Generar el PDF a `byte[]` (nunca a disco) con el exportador real.
2. Abrir con `PdfDocument.Open(bytes)`.
3. Para texto (título, columnas, valores, mensajes): concatenar `p.Text` de todas las páginas
   con `string.Join(" ", doc.GetPages().Select(p => p.Text))` y usar `Assert.Contains` /
   `Assert.DoesNotContain` sobre substrings puntuales — nunca comparar el bloque completo por
   igualdad exacta. **Excepción (ver "Correcciones post-spike" más abajo):** si el contenido
   viene de una tabla sin ancho de columna fijo y la aserción es sobre una frase de varias
   palabras, `p.Text` puede concatenar sin espacios — usar
   `doc.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text)` unido con espacio en su
   lugar. Para tokens de una sola palabra, `p.Text` sigue sirviendo sin cambios.
4. Para cantidad de páginas: `doc.NumberOfPages`.
5. Para orientación (regla de más de 6 columnas → apaisado): comparar
   `pagina.Width > pagina.Height` en la primera página.
6. Para imágenes embebidas (membrete): `doc.GetPages().Sum(p => p.GetImages().Count())`.
7. Para texto largo sin truncar: verificar que el primer Y el último fragmento significativo
   del valor original están presentes en el texto extraído (nunca aserción de igualdad
   exacta de string completo, por el motivo del punto "Lo que NO se puede afirmar").
8. Nunca un test que solo verifica ausencia de excepción — siempre al menos una aserción de
   contenido de las anteriores.

## Nota bloqueante para la Tarea 1 (no resuelta en este spike)

`StockApp.Documentos` necesita un `IFontResolver` propio con una fuente embebida como
`EmbeddedResource` (candidata: Liberation Sans, SIL OFL 1.1) asignado a
`PdfSharp.Fonts.GlobalFontSettings.FontResolver` antes de renderizar cualquier documento — sin
esto, CUALQUIER render de texto falla en cualquier plataforma (no es específico de Linux). El
plan actual no menciona este paso en ninguna tarea. Se documenta acá como hallazgo del spike;
la decisión de implementación concreta (namespace, nombres, si hace falta Italic) queda para
quien ejecute la Tarea 1, revisando este hallazgo primero.

## Código descartado

`spike-pdf/` NO se commitea. Se borra después de este spike (`rm -rf spike-pdf/`).
