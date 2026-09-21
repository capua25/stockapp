# Exportación de tablas a PDF (sub-proyecto A)

Fecha: 2026-09-18
Estado: APROBADO — pendiente de plan de implementación

## Contexto y motivación

El cliente (Intendencia de Carmelo, municipio uruguayo) pidió que todas las tablas y estadísticas del sistema se puedan exportar a PDF. El pedido se descompuso en tres sub-proyectos independientes, cada uno con su propia spec y su propio ciclo:

- **A — PDF de tablas** (esta spec).
- **B — Módulo de gráficos** (charting; hoy no existe ninguna librería de charting en el repo).
- **C — Gráficos dentro del PDF** (depende de A y B).

Orden acordado: A → B → C. Motivo: B es diseño de producto con mucho ida y vuelta; si se mezcla con A, A queda bloqueado y el cliente no recibe nada.

Requisitos del cliente para el PDF:

1. Es **para imprimir y archivar** (administración pública), no para mirar en pantalla.
2. Lleva **el dataset completo que matchea los filtros del usuario**. Los filtros y rangos de fecha se respetan. La paginación no aplica (ver estado del sistema).

## Estado verificado del sistema (hechos, no supuestos)

- **No existe paginación en StockApp, ni server-side ni client-side.** Decisión explícita documentada en `src/StockApp.Infrastructure/Repositories/DocumentoAdministrativoRepository.cs:42-46`. Todas las grillas traen el dataset completo filtrado en una sola llamada.
- Los filtros viajan como query params vía `src/StockApp.ApiClient/ApiQuery.cs:12-19` y se resuelven en SQL.
- Los `DataGridCollectionView` no filtran: son workaround de ordenamiento por click (regresión Avalonia 12). No hay un solo `.Filter` predicate en la solución.
- **Consecuencia: los 8 exports CSV actuales ya respetan los filtros al 100%**, porque exportan la colección fuente (`Items`/`Filas`/`Movimientos`), que ya es el dataset filtrado completo. El PDF puede hacer exactamente lo mismo: **no hacen falta endpoints nuevos ni re-fetch**.
- Único formato de salida hoy: **CSV**, generado 100% en el cliente vía `src/StockApp.Application/Exportacion/CsvExporter.cs` + `src/StockApp.Presentation/Services/ServicioGuardadoArchivo.cs`.
- **Cero generación de PDF** en el repo. Cero librerías de PDF o charting en `Directory.Packages.props`.

## Decisión de librería (con fundamento legal)

- **Elegida: MigraDoc (`PDFsharp-MigraDoc 6.2.4`)** — licencia MIT, target `net10.0`, API de alto nivel (Document/Section/Table/Paragraph). Costo USD 0.
- **QuestPDF descartada por motivo legal, no de precio**: su Community License dice textualmente *"Public-sector entities and publicly traded companies are not eligible, regardless of revenue"* (https://www.questpdf.com/license/community.html). Un municipio no califica sin importar su facturación. La Professional cuesta USD 1.999.
- **iText7 descartada**: AGPL v3. Con software que corre en red obliga a liberar el código fuente completo o comprar licencia comercial.
- **SelectPdf descartada**: Community limitada a 5 páginas por PDF.

## Alcance: 9 pantallas

Las 8 que hoy exportan CSV + Reporte de tareas (que hoy no tiene ninguna exportación):

1. Valorización — `src/StockApp.Presentation/ViewModels/Reportes/ValorizacionViewModel.cs`
2. Stock por categoría — `src/StockApp.Presentation/ViewModels/Reportes/StockCategoriaViewModel.cs`
3. Más movidos — `src/StockApp.Presentation/ViewModels/Reportes/MasMovidosViewModel.cs`
4. Historial por producto — `src/StockApp.Presentation/ViewModels/Reportes/HistorialPorProductoViewModel.cs`
5. Auditoría — `src/StockApp.Presentation/ViewModels/Reportes/AuditoriaLogViewModel.cs`
6. Gastos — `src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs`
7. Libro Caja — `src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs`
8. Control POA — `src/StockApp.Presentation/ViewModels/Finanzas/ControlPoaViewModel.cs`
9. Reporte de tareas — `src/StockApp.Presentation/ViewModels/Reportes/ReporteTareasViewModel.cs` (necesita que se le agreguen CSV **y** PDF)

Las otras ~30 superficies (29 DataGrid en 19 vistas + ~10 listados en ListBox/ItemsControl) quedan para una segunda vuelta mecánica, una vez validada la plantilla.

## Arquitectura

Proyecto nuevo `StockApp.Documentos` (class library, `net10.0`). Ahí y solo ahí vive la dependencia de MigraDoc.

Estructura:

```
src/StockApp.Application/Exportacion/
  ICsvExporter.cs        (ya existe)
  IPdfExporter.cs        <- interfaz nueva, sin paquetes externos
  MetadatosDocumento.cs  <- contrato de datos

src/StockApp.Documentos/   <- proyecto nuevo, acá vive MigraDoc
  PdfExporterMigraDoc.cs
  PlantillaTabular.cs
  RecursosMembrete.cs

src/StockApp.Presentation/ -> referencia a StockApp.Documentos, registra en DI
```

**Razón de la separación**: `StockApp.Application` la referencian tanto el desktop como la API. Si MigraDoc va en Application, la API del VPS Linux carga una librería de generación de documentos que nunca usa (el PDF se genera en el cliente). Además Application dejaría de ser la capa sin dependencias externas que es hoy. El desktop es API-only sin Infrastructure, así que Infrastructure no es una opción.

Contrato:

```csharp
byte[] Exportar<T>(
    IEnumerable<T> items,
    IReadOnlyList<string> columnas,
    MetadatosDocumento metadatos);
```

`MetadatosDocumento` lleva: título del reporte, descripción de los filtros aplicados, y usuario emisor. La fecha, la paginación y el logo los resuelve la plantilla.

Flujo: el ViewModel ya tiene la colección filtrada en memoria → `IPdfExporter.Exportar(...)` → `IServicioGuardadoArchivo.GuardarBytesAsync(...)` → se ofrece abrir el PDF con `ServicioAperturaArchivo`.

**Cambio necesario en código existente**: `IServicioGuardadoArchivo.GuardarBytesAsync` (interfaz en `src/StockApp.Presentation/Services/IServicioGuardadoArchivo.cs:33`, implementación en `src/StockApp.Presentation/Services/ServicioGuardadoArchivo.cs:76-96`) no expone extensión ni tipo de archivo: el `SaveFilePickerAsync` interno (líneas 85-88) solo setea `SuggestedFileName`, sin `DefaultExtension` ni `FileTypeChoices`, así que hoy el selector no ofrece ningún filtro al guardar bytes. Hay que agregarle esos parámetros para que el flujo de PDF pueda pedir extensión `.pdf` y tipo `application/pdf`, análogo a como `GuardarInternoAsync` (líneas 31-65, bloque hardcodeado en 42-54) ya lo hace fijo a CSV para el guardado de texto. El path de texto/CSV no se toca — el refactor es acotado a la firma y al cuerpo de `GuardarBytesAsync`.

Registro en DI: `src/StockApp.Presentation/App.axaml.cs` (hoy registra `ICsvExporter` en :307 y `IServicioGuardadoArchivo` en :311).

## La plantilla genérica

Una sola plantilla tabular para las 9 (y después para las 39). Solo cambian título, columnas y datos. No hay plantillas por pantalla.

**Membrete**: logo de Carmelo en negro + "INTENDENCIA DE CARMELO" + título del reporte + **descripción textual de los filtros aplicados**. Los filtros en el membrete no son decorativos: un PDF que dice "Gastos" sin aclarar el rango de fechas no es auditable.

**Cuerpo**: tabla con encabezado que se repite en cada página (MigraDoc lo soporta nativo).

**Pie**: fecha y hora de emisión + usuario que lo generó + `Pág. X de Y`.

**Logo**: `src/StockApp.Presentation/Assets/carmelo-original.png` existe y está embebido como `AvaloniaResource` (PNG 2804x1448 RGBA, ~174 KB), referenciado en `src/StockApp.Presentation/Views/LoginView.axaml:28` y `src/StockApp.Presentation/Views/InicioView.axaml:27`. Para el membrete sobre papel blanco corresponde la **variante negra**, que hoy está en la raíz del repo como `Carmelo Municipio. Negro PNG.png` (2805x1449) y no está embebida. Hay que copiarla a `Assets/` y embeberla.

**Las dos reglas de layout, en un solo lugar del código:**

- **Regla de ancho**: más de 6 columnas → A4 apaisado; 6 o menos → A4 vertical. El exportador decide solo, el usuario no configura nada. Dispara apaisado en: Gastos (11 columnas), Libro Caja (10) e Historial por producto (8).
- **Regla de texto largo**: wrap — el texto se parte en varias líneas y la fila crece en alto. **Nunca se trunca.** En papel no hay tooltip: lo que se corta, se perdió. Innegociable en un documento de auditoría que se archiva.

Son dos modos de ruptura distintos y ninguno reemplaza al otro: (1) muchas columnas — peor caso Gastos con 11 columnas y 4 de texto largo; (2) pocas columnas pero una ilimitada — Auditoría tiene solo 6 columnas pero `Detalle` es el campo libre del log, sin tope. La regla de ancho no resuelve el modo 2; por eso hacen falta las dos reglas.

## Columnas: las de la grilla, no las del CSV

El PDF lleva exactamente las columnas que el usuario ve en pantalla. **En 4 de las 9 pantallas el CSV y la grilla difieren**, y lo que sobra en el CSV son IDs internos de Postgres que en papel oficial no significan nada:

| Pantalla | Cols CSV | Cols grilla | Sobra en el CSV |
|---|---|---|---|
| Historial por producto | 12 | 8 | `MovimientoId`, `ProductoId`, `UsuarioId`, `ProductoNombre` |
| Control POA | 8 | 6 | `Ejercicio`, `Sobregirada` |
| Valorización | 7 | 6 | `ProductoId` |
| Más movidos | 5 | 4 | `ProductoId` |

Las otras 5 (Stock por categoría, Auditoría, Gastos, Libro Caja, Reporte de tareas) coinciden.

Cada pantalla define su propio orden de columnas para PDF, como constante, **separado** del `ColumnOrder`/`ColumnasCsv` del CSV. **El CSV actual no se toca** — alguien lo puede estar usando para abrir en planilla, que es un caso de uso distinto.

Conteo de columnas de grilla por pantalla, para dimensionar: Gastos 11, Libro Caja 10, Historial por producto 8, Valorización 6, Auditoría 6, Control POA 6, Reporte de tareas 6, Más movidos 4, Stock por categoría 4.

Nota sobre Reporte de tareas: sus columnas son fijas (`Clasificador, Pendientes, EnCurso, Terminadas, Canceladas, Total` — record posicional `FilaReporteTareas` en `src/StockApp.Application/Reportes/TareasReporteDtos.cs:51-57`). El agrupador cambia el contenido y la cantidad de filas, nunca las columnas. `ReporteTareasDto.TotalGeneral` (línea 66 del mismo archivo) sirve de fila de cierre.

## UI

Botón "Exportar PDF" al lado del "Exportar CSV" existente, en las 9 pantallas. Dos botones separados, un click cada uno, siguiendo el patrón del botón que ya existe (sin controles nuevos tipo SplitButton).

En Reporte de tareas hay que agregar los dos botones, porque hoy no tiene ninguno.

**Aviso de confirmación**: cuando la exportación supera ~500 filas, avisar cuántas páginas van a salir y pedir confirmación. Fundamento: sin paginación en el sistema, "exportar gastos sin filtro de fecha" dentro de tres años son miles de páginas, y alguien le va a dar imprimir.

**Apertura tras guardar**: después de guardar el PDF se ofrece abrirlo con el visor del sistema, reusando `src/StockApp.Presentation/Services/ServicioAperturaArchivo.cs:34` (que ya hace `Process.Start(new ProcessStartInfo(rutaSegura) { UseShellExecute = true })` para los adjuntos). Desde el visor el usuario obtiene la ventana de impresión completa del SO: impresora, orientación, márgenes, rango de páginas, copias, y "Microsoft Print to PDF" en Windows.

**Por qué no se usa la ventana de impresión del sistema directamente**: Avalonia no tiene API para abrir el diálogo de impresión sobre controles o vistas — no hay equivalente a `PrintDialog` (WPF) ni `PrintManager` (WinUI). El feature request es el issue AvaloniaUI/Avalonia#12567, abierto desde agosto de 2023, sin asignar y sin comentarios del equipo. Lo único que existe es `IPrintService` en `Avalonia.Controls.Pdf.Services`, cuya firma `PrintAsync(IPdfDocument, PrintOptions, CancellationToken)` **exige un PDF ya generado**. Y `System.Drawing.Printing` es Windows-only desde .NET 7, lo que rompe la compilación en Linux. Argumento de fondo: la ventana de impresión elige opciones, no genera el documento — la paginación, el membrete, el encabezado repetido y el wrap hay que construirlos igual.

## Tarea cero: spike técnico (antes de escribir la plantilla)

Dos incógnitas que no se asumen resueltas:

**(a) El logo (imagen) en Linux.** MigraDoc renderiza a través de PDFsharp, y PDFsharp tiene un problema conocido con imágenes en Linux (`System.Drawing.EnableUnixSupport` fue removido en .NET 8+, libgdiplus ya no está en distros modernas). En producción el PDF se genera en Windows, pero **los tests corren en Linux/WSL**. Verificar empíricamente que MigraDoc embebe el PNG del membrete en Linux.

**(b) Cómo se asserta el contenido de un PDF.** Un test que solo verifica "no tiró excepción" no es un test. Hay que determinar si se puede extraer el texto del PDF generado para afirmar sobre el contenido real (que el título esté, que la columna X esté, que el total sea el esperado, que haya N páginas). Si no se puede, cambia la estrategia de testing y hay que saberlo antes, no después.

## Testing

TDD, como en el resto del proyecto. Los guardianes se verifican **por mutación quitando la regla**, no razonando que funcionan: si se elimina el salto a apaisado, el test de Gastos tiene que ponerse rojo. Si no se pone rojo, ese test no custodiaba nada.

## Fuera de alcance (YAGNI)

Sin diálogo de opciones de exportación. Sin elección de orientación por el usuario. Sin plantillas por pantalla. Sin subtotales ni agrupaciones. Sin firmas. Sin marca de agua. Sin previsualización propia. Sin generación de PDF en el servidor. Sin gráficos (eso es el sub-proyecto B). Sin tocar el formato del CSV actual. Sin las otras 30 superficies.
