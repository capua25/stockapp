# Exportación de tablas a PDF — Plan de implementación

> **Para agentes ejecutores:** SUB-SKILL REQUERIDA: usá superpowers:subagent-driven-development (recomendado) o superpowers:executing-plans para implementar este plan tarea por tarea. Los pasos usan checkbox (`- [ ]`) para seguimiento.

**Objetivo:** Agregar exportación a PDF (membrete institucional, tabla con encabezado repetido, apaisado automático en tablas anchas, wrap sin truncar) a las 9 pantallas que hoy exportan CSV (más Reporte de tareas, que hoy no exporta nada), reutilizando el dataset ya filtrado que cada ViewModel tiene en memoria.

**Arquitectura:** Proyecto nuevo `StockApp.Documentos` (class library `net10.0`, sin dependencias de Avalonia) donde vive MigraDoc, referenciado solo desde `StockApp.Presentation`. El contrato `IPdfExporter`/`MetadatosDocumento` vive en `StockApp.Application.Exportacion` (sin paquetes externos, igual que `ICsvExporter`). El flujo es: ViewModel (dataset ya filtrado) → `IPdfExporter.Exportar(...)` → `IServicioGuardadoArchivo.GuardarBytesAsync(...)` → oferta de abrir con `IServicioAperturaArchivo`.

**Stack:** .NET 10 (net10.0), Avalonia 12.0.5 + CommunityToolkit.Mvvm 8.4.1, PDFsharp-MigraDoc 6.2.4 (MIT, cross-platform sin GDI+/System.Drawing), xUnit 2.5.3 + Moq 4.20.72 (Application/Presentation.Tests), Avalonia.Headless.XUnit sobre xunit.v3 (Presentation.UiTests), UglyToad.PdfPig 0.1.16 (solo en tests, para leer el contenido del PDF generado).

**Spec:** `docs/superpowers/specs/2026-09-18-export-pdf-tablas-design.md`

## Restricciones globales

- Target `.NET 10` / `net10.0` en todo proyecto nuevo, igual que el resto de la solución.
- Central Package Management: las versiones de paquete van SOLO en `Directory.Packages.props` (`ManagePackageVersionsCentrally=true`). Los `.csproj` usan `<PackageReference Include="..." />` sin `Version`.
- MigraDoc (`PDFsharp-MigraDoc`) vive únicamente en `src/StockApp.Documentos`. NUNCA en `StockApp.Application` ni en `StockApp.Api` — la API del VPS Linux nunca genera PDFs, el desktop sí.
- `UglyToad.PdfPig` es dependencia de TEST únicamente (`tests/StockApp.Documentos.Tests`), nunca de un proyecto de producción.
- Las columnas del PDF son las de la GRILLA que ve el usuario, no las del CSV. Cada pantalla define su propio orden de columnas para PDF como constante `ColumnasPdf`, separada de `ColumnOrder`/`ColumnasCsv`. El CSV actual no se toca — ni su formato, ni su comportamiento, ni sus tests.
- Regla de ancho: más de 6 columnas → A4 apaisado; 6 o menos → A4 vertical. La decide el exportador solo, sin que el usuario configure nada.
- Regla de texto largo: wrap — la fila crece en alto. Nunca se trunca contenido en un documento de auditoría que se archiva.
- `dotnet build` con varios `.csproj` puede dar falso verde si alguno falla en silencio: para verificar compilación de un proyecto puntual usar `dotnet build <ruta-al-csproj>`, nunca un build agregado de toda la solución como único chequeo.
- `dotnet sln StockApp.sln add <ruta.csproj>` para agregar proyectos nuevos a la solución — nunca editar `StockApp.sln` a mano (los GUID son generados por la herramienta).
- Commits: conventional commits en español, uno por tarea como mínimo. **NUNCA agregar "Co-Authored-By" ni ninguna atribución a IA.**
- TDD estricto: escribir el test, correrlo y verlo fallar, implementar lo mínimo, correrlo y verlo pasar, commitear.
- Los guardianes de reglas (ancho apaisado) se verifican **por mutación quitando la regla**, no razonando que funcionan: si al sacar el salto a apaisado el test de Gastos no se pone rojo, ese test no custodiaba nada.
- Español rioplatense con tildes en todo texto de usuario (títulos de PDF, mensajes de confirmación, membrete).
- Las Views de Avalonia no se auto-inicializan: toda vista nueva/tocada mantiene su enganche a `DataContextChanged` existente sin romperlo.
- `UnauthorizedAccessException` en los ViewModels de Reportes se captura en silencio vía `ViewModelBase.EjecutarCargaProtegidaAsync` — no aplica a los comandos de exportación (no llaman a la API).

---

### Tarea 0: Spike técnico — imágenes en Linux y técnica de assert de contenido PDF

**Archivos:**
- Crear (descartable, NO se commitea): `spike-pdf/SpikePdf.csproj`, `spike-pdf/Program.cs`
- Crear (persistente): `docs/superpowers/decisions/2026-09-18-spike-pdf-imagenes-y-assert.md`

**Interfaces:**
- Consume: nada (proyecto aislado, fuera de `StockApp.sln`).
- Produce: la DECISIÓN documentada que consumen las Tareas 1, 3 y 6 — qué librería de test usar para leer el PDF generado (`UglyToad.PdfPig`) y la confirmación de que MigraDoc/PDFsharp 6.2.4 embebe imágenes PNG en Linux sin `System.Drawing`.

- [ ] **Paso 1: Armar el spike descartable**

Crear `spike-pdf/SpikePdf.csproj` (proyecto standalone, fuera de la solución, con versiones de paquete explícitas porque no participa del Central Package Management):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="PDFsharp-MigraDoc" Version="6.2.4" />
    <PackageReference Include="PdfPig" Version="0.1.16" />
  </ItemGroup>

</Project>
```

Crear `spike-pdf/Program.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using UglyToad.PdfPig;

// (a) Imagen en Linux: un PNG de 1x1 rojo embebido como base64, sin tocar el filesystem para
// la imagen (mismo mecanismo "base64:" que va a usar RecursosMembrete en la Tarea 6).
const string pngRojo1x1Base64 =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

var document = new Document();
var section = document.AddSection();

var tabla = section.AddTable();
tabla.Borders.Visible = false;
var col1 = tabla.AddColumn();
var col2 = tabla.AddColumn();
var filaImagen = tabla.AddRow();
filaImagen.Cells[0].AddImage("base64:" + pngRojo1x1Base64);
filaImagen.Cells[1].AddParagraph("SPIKE-TITULO-DE-PRUEBA");

var tablaDatos = section.AddTable();
var colDatos = tablaDatos.AddColumn();
var filaHeader = tablaDatos.AddRow();
filaHeader.HeadingFormat = true;
filaHeader.Cells[0].AddParagraph("SPIKE-COLUMNA-X");
var filaDato = tablaDatos.AddRow();
filaDato.Cells[0].AddParagraph("SPIKE-VALOR-123");

var renderer = new PdfDocumentRenderer { Document = document };
renderer.RenderDocument();

using var ms = new MemoryStream();
renderer.Save(ms, false);
var bytes = ms.ToArray();

Console.WriteLine($"PDF generado: {bytes.Length} bytes.");

// (b) Assert de contenido: releer con PdfPig y verificar texto + imagen.
using var documentoLeido = PdfDocument.Open(bytes);
var textoCompleto = string.Join(" ", documentoLeido.GetPages().Select(p => p.Text));
var tieneTitulo = textoCompleto.Contains("SPIKE-TITULO-DE-PRUEBA");
var tieneColumna = textoCompleto.Contains("SPIKE-COLUMNA-X");
var tieneValor = textoCompleto.Contains("SPIKE-VALOR-123");
var cantidadImagenes = documentoLeido.GetPages().Sum(p => p.GetImages().Count());

Console.WriteLine($"Título encontrado: {tieneTitulo}");
Console.WriteLine($"Columna encontrada: {tieneColumna}");
Console.WriteLine($"Valor encontrado: {tieneValor}");
Console.WriteLine($"Cantidad de imágenes detectadas por PdfPig: {cantidadImagenes}");

if (!tieneTitulo || !tieneColumna || !tieneValor || cantidadImagenes < 1)
{
    Console.WriteLine("SPIKE FALLÓ — ver docs/superpowers/decisions/2026-09-18-spike-pdf-imagenes-y-assert.md");
    Environment.Exit(1);
}

Console.WriteLine("SPIKE OK.");
```

- [ ] **Paso 2: Ejecutar el spike y observar**

Correr (desde la raíz del repo, en Linux/WSL — el mismo entorno donde corre la suite):

```bash
cd spike-pdf && dotnet run
```

Esperado (basado en la documentación oficial de NuGet de `PDFsharp-MigraDoc` 6.2.4, que declara explícitamente "no depende de Windows y puede usarse en cualquier plataforma compatible con .NET, incluyendo Linux y macOS", sin dependencia de `System.Drawing`/GDI+ en el paquete base — a diferencia de las variantes `-GDI`/`-WPF`, que sí son Windows-only): el proceso imprime `PDF generado: N bytes.`, las tres búsquedas de texto en `true`, `Cantidad de imágenes detectadas por PdfPig: 1` (o más) y termina con `SPIKE OK.`. **Si el resultado real difiere** (imagen no detectada, excepción al renderizar, texto no legible), no seguir con la Tarea 1: hay que resolver primero (candidatos: escribir la imagen a un archivo temporal en vez de `base64:`, o cambiar de técnica de assert) y actualizar la decisión documentada del Paso 3 con lo que realmente pasó.

- [ ] **Paso 3: Documentar la decisión**

Crear `docs/superpowers/decisions/2026-09-18-spike-pdf-imagenes-y-assert.md`:

```markdown
# Spike: imágenes PDF en Linux y técnica de assert de contenido

Fecha: 2026-09-18
Contexto: Tarea 0 de docs/superpowers/plans/2026-09-18-export-pdf-tablas.md

## (a) ¿MigraDoc embebe imágenes PNG en Linux?

Sí. `PDFsharp-MigraDoc` 6.2.4 (paquete base, no las variantes `-GDI`/`-WPF`) no depende de
`System.Drawing`/GDI+: la página del paquete en NuGet.org declara explícitamente soporte para
Linux y macOS sin dependencia de Windows. Verificado empíricamente corriendo `spike-pdf/Program.cs`
en este entorno (Linux/WSL): un PNG de 1x1 embebido vía el pseudo-protocolo `"base64:" +
Convert.ToBase64String(bytes)` (pasado directo a `Cells[i].AddImage(...)`, sin tocar el
filesystem) se renderiza y PdfPig lo detecta al releer el PDF con `page.GetImages()`.

Decisión: el membrete (Tarea 6) embebe el logo como recurso `EmbeddedResource` dentro de
`StockApp.Documentos`, leído a `byte[]` y pasado a `AddImage` con el prefijo `"base64:"`. Sin
archivos temporales, sin `System.Drawing`.

## (b) ¿Cómo se asserta el contenido de un PDF generado?

Con `UglyToad.PdfPig` (MIT, gestionado, cross-platform), SOLO como dependencia de test —
nunca de producción. `PdfDocument.Open(byte[])` devuelve un documento navegable:
`document.NumberOfPages` para la cantidad de páginas, `document.GetPages()` para iterar,
`page.Text` para el texto plano de una página (concatenar todas las páginas para buscar texto
sin importar en cuál cayó) y `page.GetImages()` para detectar imágenes embebidas.

Decisión: todos los tests de `StockApp.Documentos.Tests` (Tareas 1, 3-7) usan PdfPig para
afirmar sobre título, columnas, valores, cantidad de páginas y orientación (comparando
`page.Width`/`page.Height`) — nunca un test que solo verifica ausencia de excepción.

## Código descartado

`spike-pdf/` NO se commitea. Se borra después de este spike (`rm -rf spike-pdf/`).
```

- [ ] **Paso 4: Borrar el spike descartable**

```bash
rm -rf spike-pdf/
```

- [ ] **Paso 5: Commit**

```bash
git add docs/superpowers/decisions/2026-09-18-spike-pdf-imagenes-y-assert.md
git commit -m "docs(export-pdf): documenta decision de spike sobre imagenes y assert de PDF"
```

---

### Tarea 1: Proyecto `StockApp.Documentos` — scaffolding + test de humo

**Archivos:**
- Crear: `src/StockApp.Documentos/StockApp.Documentos.csproj`
- Crear: `src/StockApp.Documentos/PdfExporterMigraDoc.cs`
- Crear: `tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj`
- Test: `tests/StockApp.Documentos.Tests/HumoPdfTests.cs`
- Modificar: `Directory.Packages.props` (agregar `PDFsharp-MigraDoc` y `PdfPig`)
- Modificar: `StockApp.sln` (vía `dotnet sln add`, no a mano)
- Modificar: `src/StockApp.Presentation/StockApp.Presentation.csproj` (agregar `ProjectReference`)

**Interfaces:**
- Consume: `StockApp.Application.Exportacion.IPdfExporter` (no existe todavía — se crea en la Tarea 2; hasta entonces `PdfExporterMigraDoc` no implementa nada, solo expone un método `Generar` de prueba).
- Produce: el proyecto `StockApp.Documentos` compilable, referenciado desde `StockApp.Presentation`, y el técnica de test (PdfPig) validada end-to-end para las Tareas 2-7.

- [ ] **Paso 1: Escribir el test que falla**

Crear `tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="PdfPig" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\StockApp.Documentos\StockApp.Documentos.csproj" />
  </ItemGroup>

</Project>
```

Crear `tests/StockApp.Documentos.Tests/HumoPdfTests.cs`:

```csharp
using System.Linq;
using StockApp.Documentos;
using UglyToad.PdfPig;
using Xunit;

namespace StockApp.Documentos.Tests;

/// <summary>
/// Test de humo (Tarea 1): prueba el pipeline completo MigraDoc → bytes → PdfPig ANTES de
/// que exista ningún contrato de negocio (IPdfExporter llega en la Tarea 2). Técnica de assert
/// decidida en la Tarea 0 (docs/superpowers/decisions/2026-09-18-spike-pdf-imagenes-y-assert.md).
/// </summary>
public class HumoPdfTests
{
    [Fact]
    public void GenerarPdfDeUnaPagina_ElTextoEsLegiblePorPdfPig()
    {
        var bytes = PdfExporterMigraDoc.GenerarPdfDeHumo("HUMO-STOCKAPP-DOCUMENTOS");

        using var documento = PdfDocument.Open(bytes);

        Assert.Equal(1, documento.NumberOfPages);
        var texto = documento.GetPages().Single().Text;
        Assert.Contains("HUMO-STOCKAPP-DOCUMENTOS", texto);
    }
}
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj`

Esperado: FALLA en la restauración/compilación — el proyecto `src/StockApp.Documentos/StockApp.Documentos.csproj` todavía no existe (`error MSB3202: The project file ... was not found` o equivalente).

- [ ] **Paso 3: Implementación mínima**

Agregar a `Directory.Packages.props`, dentro del `<ItemGroup>` existente, después del bloque `<!-- Otros -->`:

```xml
    <!-- Export PDF de tablas (spec 2026-09-18): MigraDoc solo se referencia desde
         StockApp.Documentos, nunca desde Application ni Api (ver Directory.Packages.props
         de Central Package Management). Base package, no las variantes -GDI/-WPF: no depende
         de System.Drawing/GDI+, corre en Linux/macOS (ver spike, Tarea 0). -->
    <PackageVersion Include="PDFsharp-MigraDoc" Version="6.2.4" />
    <!-- Solo para tests (StockApp.Documentos.Tests): lee el PDF generado para afirmar sobre su
         contenido real (título, columnas, cantidad de páginas, orientación) en vez de solo
         verificar ausencia de excepción. -->
    <PackageVersion Include="PdfPig" Version="0.1.16" />
```

Crear `src/StockApp.Documentos/StockApp.Documentos.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- Class library sin Avalonia: acá y solo acá vive MigraDoc (ver Restricciones globales del
       plan). Referenciada únicamente por StockApp.Presentation -- StockApp.Api nunca genera
       PDFs, el PDF se genera en el cliente desktop. -->
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="PDFsharp-MigraDoc" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\StockApp.Application\StockApp.Application.csproj" />
  </ItemGroup>

  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
      <_Parameter1>StockApp.Documentos.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>

</Project>
```

Crear `src/StockApp.Documentos/PdfExporterMigraDoc.cs` (por ahora solo con el método de humo; en la Tarea 3 se le agrega la implementación real de `IPdfExporter`, delegando a `PlantillaTabular`):

```csharp
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;

namespace StockApp.Documentos;

/// <summary>
/// Implementación de <c>IPdfExporter</c> (el contrato llega en la Tarea 2) basada en MigraDoc.
/// Por ahora solo expone <see cref="GenerarPdfDeHumo"/>, usado por el test de humo de la
/// Tarea 1 para validar el pipeline Document → PdfDocumentRenderer → bytes antes de introducir
/// ningún contrato de negocio.
/// </summary>
public static class PdfExporterMigraDoc
{
    /// <summary>Genera un PDF de una sola página con un único párrafo de texto. Descartado por
    /// las tareas siguientes, que reemplazan el cuerpo real vía <c>PlantillaTabular</c>.</summary>
    public static byte[] GenerarPdfDeHumo(string texto)
    {
        var document = new Document();
        var section = document.AddSection();
        section.AddParagraph(texto);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var ms = new MemoryStream();
        renderer.Save(ms, false);
        return ms.ToArray();
    }
}
```

Agregar ambos proyectos a la solución y la referencia desde Presentation:

```bash
dotnet sln StockApp.sln add src/StockApp.Documentos/StockApp.Documentos.csproj
dotnet sln StockApp.sln add tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj
dotnet add src/StockApp.Presentation/StockApp.Presentation.csproj reference src/StockApp.Documentos/StockApp.Documentos.csproj
```

Esto agrega a `src/StockApp.Presentation/StockApp.Presentation.csproj`, en el `<ItemGroup>` de `ProjectReference` (junto a `StockApp.ApiClient`, `StockApp.Application`, `StockApp.Configuracion`):

```xml
    <ProjectReference Include="..\StockApp.Documentos\StockApp.Documentos.csproj" />
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj`

Esperado: PASA (1/1).

- [ ] **Paso 5: Commit**

```bash
git add Directory.Packages.props StockApp.sln src/StockApp.Documentos tests/StockApp.Documentos.Tests src/StockApp.Presentation/StockApp.Presentation.csproj
git commit -m "feat(export-pdf): crea el proyecto StockApp.Documentos con MigraDoc y test de humo"
```

---

### Tarea 2: Contrato `IPdfExporter` + `MetadatosDocumento` en Application

**Archivos:**
- Crear: `src/StockApp.Application/Exportacion/MetadatosDocumento.cs`
- Crear: `src/StockApp.Application/Exportacion/IPdfExporter.cs`
- Test: `tests/StockApp.Application.Tests/Exportacion/MetadatosDocumentoTests.cs`

**Interfaces:**
- Consume: nada (sin paquetes externos, igual que `ICsvExporter`).
- Produce (consumido desde la Tarea 3 en adelante):
  - `record MetadatosDocumento(string Titulo, string DescripcionFiltros, string UsuarioEmisor)`
  - `interface IPdfExporter { byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnas, MetadatosDocumento metadatos); }`

- [ ] **Paso 1: Escribir el test que falla**

Crear `tests/StockApp.Application.Tests/Exportacion/MetadatosDocumentoTests.cs`:

```csharp
using StockApp.Application.Exportacion;
using Xunit;

namespace StockApp.Application.Tests.Exportacion;

public class MetadatosDocumentoTests
{
    [Fact]
    public void Constructor_GuardaLosTresValoresProvistos()
    {
        var metadatos = new MetadatosDocumento(
            Titulo: "Valorización de inventario",
            DescripcionFiltros: "Sin filtros aplicados.",
            UsuarioEmisor: "admin");

        Assert.Equal("Valorización de inventario", metadatos.Titulo);
        Assert.Equal("Sin filtros aplicados.", metadatos.DescripcionFiltros);
        Assert.Equal("admin", metadatos.UsuarioEmisor);
    }

    /// <summary>
    /// El contrato IPdfExporter debe poder implementarse con la firma exacta que van a usar
    /// las Tareas 3-20: genérico sobre T, recibe columnas + metadatos, devuelve byte[]. Este
    /// test compila (y falla si alguien cambia la firma) usando un fake local mínimo.
    /// </summary>
    private sealed record FilaDePrueba(string Nombre);

    private sealed class FakePdfExporter : IPdfExporter
    {
        public byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnas, MetadatosDocumento metadatos)
            => new byte[] { 1, 2, 3 };
    }

    [Fact]
    public void IPdfExporter_SePuedeImplementarConLaFirmaEsperada()
    {
        IPdfExporter exportador = new FakePdfExporter();
        var items = new[] { new FilaDePrueba("x") };
        var metadatos = new MetadatosDocumento("t", "d", "u");

        var resultado = exportador.Exportar(items, new[] { "Nombre" }, metadatos);

        Assert.Equal(new byte[] { 1, 2, 3 }, resultado);
    }
}
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter "FullyQualifiedName~MetadatosDocumentoTests"`

Esperado: FALLA en compilación — `MetadatosDocumento` e `IPdfExporter` no existen (`CS0246`).

- [ ] **Paso 3: Implementación mínima**

Crear `src/StockApp.Application/Exportacion/MetadatosDocumento.cs`:

```csharp
namespace StockApp.Application.Exportacion;

/// <summary>
/// Metadatos del membrete de un export PDF (spec 2026-09-18): título del reporte, descripción
/// textual de los filtros aplicados (no decorativa — un PDF que dice "Gastos" sin aclarar el
/// rango de fechas no es auditable) y usuario que lo generó. La fecha de emisión, la
/// paginación y el logo los resuelve la plantilla (<c>PlantillaTabular</c> en
/// StockApp.Documentos), no el llamador.
/// </summary>
public sealed record MetadatosDocumento(
    string Titulo,
    string DescripcionFiltros,
    string UsuarioEmisor);
```

Crear `src/StockApp.Application/Exportacion/IPdfExporter.cs`:

```csharp
namespace StockApp.Application.Exportacion;

/// <summary>
/// Exportador PDF genérico para tablas (spec 2026-09-18). Análogo a <see cref="ICsvExporter"/>
/// pero con membrete institucional, encabezado de tabla repetido por página, apaisado
/// automático en tablas anchas y wrap de texto sin truncar. La implementación real
/// (<c>PdfExporterMigraDoc</c>) vive en StockApp.Documentos -- este contrato vive en
/// Application, sin paquetes externos, porque tanto el desktop como (potencialmente) otros
/// clientes futuros lo consumen sin necesitar la dependencia de MigraDoc.
/// </summary>
public interface IPdfExporter
{
    /// <summary>
    /// Genera el PDF completo de <paramref name="items"/> con las columnas indicadas.
    /// </summary>
    /// <typeparam name="T">Tipo de los items a exportar.</typeparam>
    /// <param name="items">Colección de items YA FILTRADA -- la misma que ve la grilla.</param>
    /// <param name="columnas">
    /// Nombres de las propiedades a exportar, en el orden de la GRILLA (no del CSV). Más de 6
    /// columnas dispara apaisado automático.
    /// </param>
    /// <param name="metadatos">Título, descripción de filtros y usuario emisor del membrete.</param>
    /// <returns>El PDF completo como array de bytes.</returns>
    byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnas, MetadatosDocumento metadatos);
}
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter "FullyQualifiedName~MetadatosDocumentoTests"`

Esperado: PASA (2/2).

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Application/Exportacion/MetadatosDocumento.cs src/StockApp.Application/Exportacion/IPdfExporter.cs tests/StockApp.Application.Tests/Exportacion/MetadatosDocumentoTests.cs
git commit -m "feat(export-pdf): agrega el contrato IPdfExporter y MetadatosDocumento en Application"
```

---

### Tarea 3: `PlantillaTabular` — tabla básica con encabezado repetido por página

**Archivos:**
- Crear: `src/StockApp.Documentos/PlantillaTabular.cs`
- Modificar: `src/StockApp.Documentos/PdfExporterMigraDoc.cs` (implementa `IPdfExporter` de verdad, delega a `PlantillaTabular`; se elimina `GenerarPdfDeHumo`)
- Modificar: `tests/StockApp.Documentos.Tests/HumoPdfTests.cs` → renombrar a `tests/StockApp.Documentos.Tests/PdfExporterMigraDocTests.cs`
- Test: `tests/StockApp.Documentos.Tests/PlantillaTabularTests.cs`

**Interfaces:**
- Consume: `StockApp.Application.Exportacion.IPdfExporter`, `MetadatosDocumento` (Tarea 2).
- Produce (consumido por las Tareas 4-9):
  - `class PlantillaTabular { byte[] Generar<T>(IEnumerable<T> items, IReadOnlyList<string> columnas, MetadatosDocumento metadatos); }`
  - `class PdfExporterMigraDoc : IPdfExporter` (registrable en DI desde la Tarea 9).

- [ ] **Paso 1: Escribir el test que falla**

Borrar `tests/StockApp.Documentos.Tests/HumoPdfTests.cs` (el humo cumplió su función en la Tarea 1) y crear `tests/StockApp.Documentos.Tests/PlantillaTabularTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using StockApp.Application.Exportacion;
using StockApp.Documentos;
using UglyToad.PdfPig;
using Xunit;

namespace StockApp.Documentos.Tests;

public class PlantillaTabularTests
{
    private sealed record FilaSimple(string Codigo, string Nombre);

    private static MetadatosDocumento Metadatos(string titulo = "Reporte de prueba") =>
        new(titulo, "Sin filtros aplicados.", "admin");

    [Fact]
    public void Generar_ConDatos_ElHeaderYLosValoresSonLegibles()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar"), new FilaSimple("P002", "Harina") };
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, new[] { "Codigo", "Nombre" }, Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));
        Assert.Contains("Codigo", texto);
        Assert.Contains("Nombre", texto);
        Assert.Contains("P001", texto);
        Assert.Contains("Azúcar", texto);
        Assert.Contains("P002", texto);
        Assert.Contains("Harina", texto);
    }

    [Fact]
    public void Generar_ColeccionVacia_SoloTieneElHeader()
    {
        var items = System.Array.Empty<FilaSimple>();
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, new[] { "Codigo", "Nombre" }, Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));
        Assert.Contains("Codigo", texto);
        Assert.Contains("Nombre", texto);
    }

    [Fact]
    public void Generar_ColumnaInexistente_LanzaArgumentException()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var plantilla = new PlantillaTabular();

        var ex = Assert.Throws<ArgumentException>(
            () => plantilla.Generar(items, new[] { "Codigo", "NoExiste" }, Metadatos()));

        Assert.Contains("NoExiste", ex.Message);
    }

    /// <summary>
    /// Guardián del encabezado repetido (MigraDoc HeadingFormat, nativo): con suficientes filas
    /// para forzar 2+ páginas, "Codigo" tiene que aparecer en AMBAS páginas, no solo en la primera.
    /// </summary>
    [Fact]
    public void Generar_ConSuficientesFilasParaDosPaginas_ElHeaderApareceEnAmbasPaginas()
    {
        var items = Enumerable.Range(1, 80)
            .Select(i => new FilaSimple($"P{i:0000}", $"Producto número {i}"))
            .ToList();
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, new[] { "Codigo", "Nombre" }, Metadatos());

        using var documento = PdfDocument.Open(pdf);
        Assert.True(documento.NumberOfPages >= 2, $"Se esperaban 2+ páginas, hubo {documento.NumberOfPages}.");

        foreach (var pagina in documento.GetPages())
            Assert.Contains("Codigo", pagina.Text);
    }

    /// <summary>Contrato IPdfExporter cumplido por PdfExporterMigraDoc, delegando a PlantillaTabular.</summary>
    [Fact]
    public void PdfExporterMigraDoc_ImplementaIPdfExporter_YGeneraContenidoLegible()
    {
        IPdfExporter exportador = new PdfExporterMigraDoc();
        var items = new[] { new FilaSimple("P001", "Azúcar") };

        var pdf = exportador.Exportar(items, new[] { "Codigo", "Nombre" }, Metadatos("Título del exportador"));

        using var documento = PdfDocument.Open(pdf);
        var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));
        Assert.Contains("P001", texto);
    }
}
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj`

Esperado: FALLA en compilación — `PlantillaTabular` no existe todavía.

- [ ] **Paso 3: Implementación mínima**

Crear `src/StockApp.Documentos/PlantillaTabular.cs`:

```csharp
using System.Globalization;
using System.Reflection;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using StockApp.Application.Exportacion;

namespace StockApp.Documentos;

/// <summary>
/// Plantilla tabular genérica para exportar cualquier colección a PDF (spec 2026-09-18): una
/// sola plantilla para las 9 pantallas de este plan (y las ~30 futuras) -- solo cambian título,
/// columnas y datos. El encabezado de la tabla se repite en cada página vía
/// <c>Row.HeadingFormat = true</c>, nativo de MigraDoc.
///
/// Esta clase crece de forma incremental a lo largo de las Tareas 3-7 del plan: acá solo se
/// resuelve la tabla básica con encabezado repetido. El apaisado automático (Tarea 4), el wrap
/// sin truncar (Tarea 5, ya nativo de MigraDoc), el membrete (Tarea 6) y el pie de página
/// (Tarea 7) se agregan sobre este mismo método.
/// </summary>
public sealed class PlantillaTabular
{
    private const string FormatoFecha = "dd/MM/yyyy HH:mm:ss";
    private const string FormatoFechaSolo = "dd/MM/yyyy";

    public byte[] Generar<T>(
        IEnumerable<T> items,
        IReadOnlyList<string> columnas,
        MetadatosDocumento metadatos)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(columnas);
        ArgumentNullException.ThrowIfNull(metadatos);

        var propiedades = ResolverPropiedades<T>(columnas);

        var document = new Document();
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;

        var tabla = section.AddTable();
        tabla.Borders.Width = 0.5;

        foreach (var _ in columnas)
            tabla.AddColumn();

        var filaEncabezado = tabla.AddRow();
        filaEncabezado.HeadingFormat = true;
        filaEncabezado.Format.Font.Bold = true;
        for (var i = 0; i < columnas.Count; i++)
            filaEncabezado.Cells[i].AddParagraph(columnas[i]);

        foreach (var item in items)
        {
            var fila = tabla.AddRow();
            for (var i = 0; i < columnas.Count; i++)
            {
                var valor = propiedades[i].GetValue(item);
                fila.Cells[i].AddParagraph(FormatearValor(valor));
            }
        }

        return Renderizar(document);
    }

    private static PropertyInfo[] ResolverPropiedades<T>(IReadOnlyList<string> columnas)
        => columnas
            .Select(nombre =>
                typeof(T).GetProperty(nombre, BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new ArgumentException(
                        $"La propiedad '{nombre}' no existe en {typeof(T).Name}.", nameof(columnas)))
            .ToArray();

    /// <summary>
    /// Mismo criterio de fechas que <c>CsvExporter.FormatearValor</c> (bugfix de huso horario:
    /// DateTime se persiste en UTC pero se muestra en hora local; DateOnly NO se convierte,
    /// representa un día-calendario sin instante). Suma formato invariante explícito para
    /// decimales y enteros: un documento impreso/archivado no puede depender de la cultura del
    /// SO que lo generó (decisión del proyecto: decimales con punto, ver memoria
    /// "Uruguay, no Argentina").
    /// </summary>
    private static string FormatearValor(object? valor) => valor switch
    {
        null => string.Empty,
        DateTime fecha => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
            .ToLocalTime()
            .ToString(FormatoFecha, CultureInfo.InvariantCulture),
        DateOnly fecha => fecha.ToString(FormatoFechaSolo, CultureInfo.InvariantCulture),
        decimal numero => numero.ToString("N2", CultureInfo.InvariantCulture),
        int numero => numero.ToString(CultureInfo.InvariantCulture),
        _ => valor.ToString() ?? string.Empty,
    };

    private static byte[] Renderizar(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var ms = new MemoryStream();
        renderer.Save(ms, false);
        return ms.ToArray();
    }
}
```

Reemplazar el contenido de `src/StockApp.Documentos/PdfExporterMigraDoc.cs`:

```csharp
using StockApp.Application.Exportacion;

namespace StockApp.Documentos;

/// <summary>
/// Implementación de <see cref="IPdfExporter"/> basada en MigraDoc. Delega toda la
/// construcción del documento a <see cref="PlantillaTabular"/> -- esta clase es solo el punto
/// de entrada que se registra en DI (Tarea 9).
/// </summary>
public sealed class PdfExporterMigraDoc : IPdfExporter
{
    private readonly PlantillaTabular _plantilla = new();

    public byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnas, MetadatosDocumento metadatos)
        => _plantilla.Generar(items, columnas, metadatos);
}
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj`

Esperado: PASA (5/5).

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Documentos tests/StockApp.Documentos.Tests
git commit -m "feat(export-pdf): agrega PlantillaTabular con encabezado de tabla repetido por pagina"
```

---

### Tarea 4: Regla de ancho — más de 6 columnas → A4 apaisado

**Archivos:**
- Modificar: `src/StockApp.Documentos/PlantillaTabular.cs` (agrega `section.PageSetup.Orientation`)
- Test: `tests/StockApp.Documentos.Tests/PlantillaTabularTests.cs` (agrega 2 tests)

**Interfaces:**
- Consume: `PlantillaTabular.Generar<T>` (Tarea 3), sin cambios de firma.
- Produce: el mismo método, ahora con orientación decidida por `columnas.Count`. Sin impacto en las Tareas 12-20 (la orientación es automática, ninguna pantalla la configura).

- [ ] **Paso 1: Escribir el test que falla**

Agregar a `tests/StockApp.Documentos.Tests/PlantillaTabularTests.cs`:

```csharp
    [Fact]
    public void Generar_ConSeisColumnasOMenos_UsaOrientacionVertical()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, new[] { "Codigo", "Nombre" }, Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);
        Assert.True(pagina.Height > pagina.Width, "6 columnas o menos debe ser A4 vertical.");
    }

    [Fact]
    public void Generar_ConMasDeSeisColumnas_UsaOrientacionApaisada()
    {
        var items = new[] { new FilaOnceColumnas() };
        var columnas = new[]
        {
            "C1", "C2", "C3", "C4", "C5", "C6", "C7",
        };
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, columnas, Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);
        Assert.True(pagina.Width > pagina.Height, "Más de 6 columnas debe ser A4 apaisado.");
    }

    private sealed record FilaOnceColumnas(
        string C1 = "a", string C2 = "b", string C3 = "c", string C4 = "d",
        string C5 = "e", string C6 = "f", string C7 = "g");
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj --filter "FullyQualifiedName~Orientacion"`

Esperado: FALLA `Generar_ConMasDeSeisColumnas_UsaOrientacionApaisada` — la Tarea 3 siempre genera A4 vertical (`section.PageSetup.Orientation` nunca se toca, por lo que MigraDoc usa `Orientation.Portrait` por defecto).

- [ ] **Paso 3: Implementación mínima**

En `src/StockApp.Documentos/PlantillaTabular.cs`, dentro de `Generar<T>`, agregar la línea de orientación justo después de `section.PageSetup.PageFormat = PageFormat.A4;`:

```csharp
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = columnas.Count > 6 ? Orientation.Landscape : Orientation.Portrait;
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj --filter "FullyQualifiedName~Orientacion"`

Esperado: PASA (2/2).

**Verificación por mutación (obligatoria, no opcional):** comentar temporalmente la línea de orientación (dejar solo `section.PageSetup.PageFormat = PageFormat.A4;`, sin tocar `Orientation`) y volver a correr el mismo filtro. Esperado: `Generar_ConMasDeSeisColumnas_UsaOrientacionApaisada` se pone ROJO. Si sigue en verde, el test no está custodiando la regla — revisar la aserción antes de continuar. Restaurar la línea y confirmar que vuelve a estar en verde antes de seguir.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Documentos/PlantillaTabular.cs tests/StockApp.Documentos.Tests/PlantillaTabularTests.cs
git commit -m "feat(export-pdf): agrega apaisado automatico para tablas de mas de 6 columnas"
```

---

### Tarea 5: Regla de texto largo — wrap, nunca truncar

**Archivos:**
- Modificar: `src/StockApp.Documentos/PlantillaTabular.cs` (sin cambios de lógica esperados — ver Paso 3)
- Test: `tests/StockApp.Documentos.Tests/PlantillaTabularTests.cs` (agrega 1 test)

**Interfaces:**
- Consume: `PlantillaTabular.Generar<T>` (Tareas 3-4), sin cambios de firma.
- Produce: garantía verificada de que texto largo (caso extremo: `Detalle` libre de Auditoría, sin tope de longitud) aparece completo en el PDF.

- [ ] **Paso 1: Escribir el test que falla**

Agregar a `tests/StockApp.Documentos.Tests/PlantillaTabularTests.cs`:

```csharp
    private sealed record FilaConTextoLargo(string Nombre, string Detalle);

    [Fact]
    public void Generar_ConTextoMuyLargo_ApareceCompletoSinTruncar()
    {
        var textoLargo = string.Join(" ", Enumerable.Range(1, 60).Select(i => $"palabra{i:000}"));
        var items = new[] { new FilaConTextoLargo("Fila 1", textoLargo) };
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, new[] { "Nombre", "Detalle" }, Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));

        // El contenido completo tiene que estar -- primera palabra, última palabra, y una del
        // medio. Un truncado dejaría "palabra001" pero no "palabra060".
        Assert.Contains("palabra001", texto);
        Assert.Contains("palabra030", texto);
        Assert.Contains("palabra060", texto);
    }
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj --filter "FullyQualifiedName~TextoMuyLargo"`

Esperado: PASA de entrada, en verde. `PlantillaTabular.Generar` usa `Cells[i].AddParagraph(...)` sin ningún límite de longitud ni `TextTrimming` -- MigraDoc no trunca texto de tabla por diseño (una celda crece en alto para lo que haga falta, a diferencia de un `DataGridTextColumn` de Avalonia). No hay "rojo" que forzar porque no hay código de truncado que quitar: este test queda como guardián de regresión, no como ciclo TDD clásico. Documentarlo así en el mensaje del commit del Paso 5 en vez de fingir un ciclo rojo que no existe.

**Verificación de que el guardián no es un placebo:** editar temporalmente `FormatearValor` (o el `AddParagraph`) para forzar un truncado artificial, por ejemplo `fila.Cells[i].AddParagraph(FormatearValor(valor) is { Length: > 50 } s ? s[..50] : FormatearValor(valor))`, y confirmar que el test se pone ROJO (falta `"palabra060"`). Revertir el cambio inmediatamente después de confirmarlo.

- [ ] **Paso 3: Implementación mínima**

Ninguna: el comportamiento ya es correcto (ver Paso 2). No agregar código de recorte "por las dudas" -- sería introducir la regla prohibida que este mismo test existe para prevenir.

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj --filter "FullyQualifiedName~TextoMuyLargo"`

Esperado: PASA (1/1).

- [ ] **Paso 5: Commit**

```bash
git add tests/StockApp.Documentos.Tests/PlantillaTabularTests.cs
git commit -m "test(export-pdf): agrega guardian de regresion para texto largo sin truncar"
```

---

### Tarea 6: Membrete — logo de Carmelo, título y descripción de filtros

**Archivos:**
- Crear: `src/StockApp.Documentos/RecursosMembrete.cs`
- Crear: `src/StockApp.Documentos/Assets/carmelo-negro.png` (copiado de `Carmelo Municipio. Negro PNG.png`, raíz del repo)
- Modificar: `src/StockApp.Documentos/StockApp.Documentos.csproj` (agrega `EmbeddedResource`)
- Modificar: `src/StockApp.Documentos/PlantillaTabular.cs` (agrega `AgregarMembrete`)
- Test: `tests/StockApp.Documentos.Tests/PlantillaTabularTests.cs` (agrega 3 tests)
- Test: `tests/StockApp.Documentos.Tests/RecursosMembreteTests.cs`

**Interfaces:**
- Consume: `PlantillaTabular.Generar<T>` (Tareas 3-5), sin cambios de firma. `MetadatosDocumento.Titulo`/`DescripcionFiltros` (Tarea 2).
- Produce: `internal static class RecursosMembrete { internal static byte[] ObtenerLogoNegro(); }`, consumido solo desde `PlantillaTabular`.

**Decisión de ubicación del recurso (pedida explícitamente por el plan):** el logo vive DENTRO de `StockApp.Documentos`, como `EmbeddedResource`, no en `StockApp.Presentation/Assets` (`AvaloniaResource`). Razón: `StockApp.Documentos` no depende de Avalonia (ver Restricciones globales) -- si el logo viviera en Presentation, `PlantillaTabular` necesitaría que Presentation se lo pasara como parámetro (un `byte[]` extra en cada llamada a `Generar`, o una nueva dependencia de `StockApp.Documentos` hacia Avalonia solo para leer `AvaloniaResource` vía `AssetLoader`). Manteniéndolo embebido en el propio proyecto, `StockApp.Documentos.Tests` puede probar el membrete completo (logo incluido) sin levantar ninguna infraestructura de Avalonia, y el proyecto sigue siendo autocontenido: cualquier cliente futuro que use `IPdfExporter` obtiene el membrete completo sin tener que proveerle el logo.

- [ ] **Paso 1: Escribir el test que falla**

Copiar el logo (fuera del ciclo TDD, es un asset):

```bash
mkdir -p src/StockApp.Documentos/Assets
cp "Carmelo Municipio. Negro PNG.png" src/StockApp.Documentos/Assets/carmelo-negro.png
```

Crear `tests/StockApp.Documentos.Tests/RecursosMembreteTests.cs`:

```csharp
using StockApp.Documentos;
using Xunit;

namespace StockApp.Documentos.Tests;

public class RecursosMembreteTests
{
    [Fact]
    public void ObtenerLogoNegro_DevuelveBytesDeUnPngValido()
    {
        var bytes = RecursosMembreteTestAccessor.ObtenerLogoNegro();

        Assert.NotEmpty(bytes);
        // Firma PNG: 0x89 'P' 'N' 'G' \r \n 0x1A \n
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
    }
}
```

`RecursosMembrete` es `internal`: agregar el accessor de test en `src/StockApp.Documentos/PdfExporterMigraDoc.cs` no corresponde -- en su lugar, exponerlo vía el `InternalsVisibleTo` ya declarado en la Tarea 1 (`StockApp.Documentos.Tests` ya puede ver internals). Reemplazar en el test de arriba `RecursosMembreteTestAccessor.ObtenerLogoNegro()` por `RecursosMembrete.ObtenerLogoNegro()` directo (funciona porque el test project tiene visibilidad de internals):

```csharp
        var bytes = RecursosMembrete.ObtenerLogoNegro();
```

Agregar a `tests/StockApp.Documentos.Tests/PlantillaTabularTests.cs`:

```csharp
    [Fact]
    public void Generar_IncluyeElTituloDelMetadato()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, new[] { "Codigo", "Nombre" }, Metadatos("Valorización de inventario"));

        using var documento = PdfDocument.Open(pdf);
        var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));
        Assert.Contains("Valorización de inventario", texto);
        Assert.Contains("INTENDENCIA DE CARMELO", texto);
    }

    [Fact]
    public void Generar_IncluyeLaDescripcionDeFiltros()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var plantilla = new PlantillaTabular();
        var metadatos = new MetadatosDocumento("Título", "Período: 01/01/2026 a 31/12/2026.", "admin");

        var pdf = plantilla.Generar(items, new[] { "Codigo", "Nombre" }, metadatos);

        using var documento = PdfDocument.Open(pdf);
        var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));
        Assert.Contains("Período: 01/01/2026 a 31/12/2026.", texto);
    }

    [Fact]
    public void Generar_ElMembreteIncluyeUnaImagen()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, new[] { "Codigo", "Nombre" }, Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var cantidadImagenes = documento.GetPages().Sum(p => p.GetImages().Count());
        Assert.True(cantidadImagenes >= 1, "El membrete debe incluir el logo.");
    }
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj --filter "FullyQualifiedName~Membrete|FullyQualifiedName~IncluyeElTitulo|FullyQualifiedName~IncluyeLaDescripcion"`

Esperado: FALLA — `RecursosMembrete` no existe, y `PlantillaTabular.Generar` no agrega ni "INTENDENCIA DE CARMELO" ni la descripción de filtros ni ninguna imagen.

- [ ] **Paso 3: Implementación mínima**

En `src/StockApp.Documentos/StockApp.Documentos.csproj`, agregar un `<ItemGroup>`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Assets\carmelo-negro.png" />
  </ItemGroup>
```

Crear `src/StockApp.Documentos/RecursosMembrete.cs`:

```csharp
namespace StockApp.Documentos;

/// <summary>
/// Logo institucional para el membrete, embebido DENTRO de StockApp.Documentos (no en
/// StockApp.Presentation/Assets): este proyecto no depende de Avalonia (ver Restricciones
/// globales del plan), así que el recurso vive como <c>EmbeddedResource</c> del propio
/// ensamblado -- StockApp.Documentos queda autocontenido y testeable sin levantar Avalonia.
/// Variante negra (no la de fondo con transparencia usada en Login/Inicio): sobre papel blanco
/// corresponde el logo negro, no el de marca de agua.
/// </summary>
internal static class RecursosMembrete
{
    private const string RutaRecurso = "StockApp.Documentos.Assets.carmelo-negro.png";

    internal static byte[] ObtenerLogoNegro()
    {
        using var stream = typeof(RecursosMembrete).Assembly.GetManifestResourceStream(RutaRecurso)
            ?? throw new InvalidOperationException($"No se encontró el recurso embebido '{RutaRecurso}'.");

        using var memoria = new MemoryStream();
        stream.CopyTo(memoria);
        return memoria.ToArray();
    }
}
```

En `src/StockApp.Documentos/PlantillaTabular.cs`, agregar el `using System;` si falta (para `Convert`) y, dentro de `Generar<T>`, llamar a `AgregarMembrete` justo después de `section.PageSetup.Orientation = ...;` y antes de `var tabla = section.AddTable();`:

```csharp
        AgregarMembrete(section, metadatos);

        var tabla = section.AddTable();
```

Agregar el método privado:

```csharp
    private static void AgregarMembrete(Section section, MetadatosDocumento metadatos)
    {
        var tablaMembrete = section.AddTable();
        tablaMembrete.Borders.Visible = false;
        tablaMembrete.AddColumn(Unit.FromCentimeter(3));
        tablaMembrete.AddColumn();

        var fila = tablaMembrete.AddRow();

        var logoBase64 = "base64:" + Convert.ToBase64String(RecursosMembrete.ObtenerLogoNegro());
        var imagen = fila.Cells[0].AddImage(logoBase64);
        imagen.Width = Unit.FromCentimeter(2.5);
        imagen.LockAspectRatio = true;

        var celdaTexto = fila.Cells[1];
        celdaTexto.AddParagraph("INTENDENCIA DE CARMELO").Format.Font.Bold = true;
        var parrafoTitulo = celdaTexto.AddParagraph(metadatos.Titulo);
        parrafoTitulo.Format.Font.Size = 14;
        var parrafoFiltros = celdaTexto.AddParagraph(metadatos.DescripcionFiltros);
        parrafoFiltros.Format.Font.Size = 9;

        section.AddParagraph(); // separación antes de la tabla de datos
    }
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj`

Esperado: PASA (todos los tests del proyecto, incluidos los de las Tareas 3-5).

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Documentos tests/StockApp.Documentos.Tests
git commit -m "feat(export-pdf): agrega membrete con logo, titulo y descripcion de filtros"
```

---

### Tarea 7: Pie de página — fecha de emisión, usuario, Pág. X de Y

**Archivos:**
- Modificar: `src/StockApp.Documentos/PlantillaTabular.cs` (agrega `AgregarPie`; cambia la firma interna del helper para recibir `metadatos.UsuarioEmisor`)
- Test: `tests/StockApp.Documentos.Tests/PlantillaTabularTests.cs` (agrega 1 test)

**Interfaces:**
- Consume: `PlantillaTabular.Generar<T>` (Tareas 3-6), sin cambios de firma pública.
- Produce: el mismo método, ahora con pie de página en cada sección.

- [ ] **Paso 1: Escribir el test que falla**

Agregar a `tests/StockApp.Documentos.Tests/PlantillaTabularTests.cs`:

```csharp
    [Fact]
    public void Generar_ElPieIncluyeUsuarioEmisorYNumeracionDePagina()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var plantilla = new PlantillaTabular();
        var metadatos = new MetadatosDocumento("Título", "Sin filtros aplicados.", "juan.perez");

        var pdf = plantilla.Generar(items, new[] { "Codigo", "Nombre" }, metadatos);

        using var documento = PdfDocument.Open(pdf);
        var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));
        Assert.Contains("juan.perez", texto);
        Assert.Contains("Pág.", texto);
    }
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj --filter "FullyQualifiedName~PieIncluye"`

Esperado: FALLA — no hay pie de página todavía.

- [ ] **Paso 3: Implementación mínima**

En `src/StockApp.Documentos/PlantillaTabular.cs`, dentro de `Generar<T>`, agregar la llamada justo antes del `return Renderizar(document);`:

```csharp
        AgregarPie(section, metadatos.UsuarioEmisor);

        return Renderizar(document);
```

Agregar el método privado:

```csharp
    private static void AgregarPie(Section section, string usuarioEmisor)
    {
        var parrafo = section.Footers.Primary.AddParagraph();
        parrafo.Format.Font.Size = 8;
        parrafo.AddText($"Emitido el {DateTime.Now:dd/MM/yyyy HH:mm} por {usuarioEmisor}   —   Pág. ");
        parrafo.AddPageField();
        parrafo.AddText(" de ");
        parrafo.AddNumPagesField();
    }
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj`

Esperado: PASA (todos los tests del proyecto). Con esto se cierra la plantilla genérica: `StockApp.Documentos` queda completo para las Tareas 8-11 (servicios de guardado/apertura en Presentation) y 12-20 (wiring por pantalla).

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Documentos/PlantillaTabular.cs tests/StockApp.Documentos.Tests/PlantillaTabularTests.cs
git commit -m "feat(export-pdf): agrega pie de pagina con fecha, usuario emisor y numeracion"
```

---

### Tarea 8: `ServicioGuardadoArchivo` — exponer extensión y tipo en `GuardarBytesAsync`

**Archivos:**
- Modificar: `src/StockApp.Presentation/Services/IServicioGuardadoArchivo.cs`
- Modificar: `src/StockApp.Presentation/Services/ServicioGuardadoArchivo.cs`
- Modificar: `tests/StockApp.Presentation.UiTests/MantenimientoViewTests.cs` (fake, agrega 2 parámetros a la firma)
- Modificar: `tests/StockApp.Presentation.UiTests/LibroCajaViewTests.cs` (ídem)
- Modificar: `tests/StockApp.Presentation.UiTests/AccesoLimitadoViewTests.cs` (ídem)
- Modificar: `tests/StockApp.Presentation.UiTests/GastosViewTests.cs` (ídem)
- Modificar: `tests/StockApp.Presentation.UiTests/CargaProtegidaEstadoVacioUiTests.cs` (ídem)
- Modificar: `tests/StockApp.Presentation.UiTests/SignoNegativoBadgeTests.cs` (ídem)
- Test: `tests/StockApp.Presentation.Tests/Services/ServicioGuardadoArchivoCompatibilidadTests.cs`

**Interfaces:**
- Consume: nada nuevo.
- Produce (consumido por la Tarea 9 en adelante): `Task<bool> GuardarBytesAsync(Stream contenido, string nombreSugerido, CancellationToken ct = default, string? extension = null, string? tipoMime = null)`.

**Diseño de compatibilidad (por qué los 2 parámetros nuevos van DESPUÉS de `ct`, no antes):** hoy `GuardarBytesAsync` tiene 2 call sites reales, ambos en `MantenimientoViewModel.cs` (backups/logs), y 6 fakes de `IServicioGuardadoArchivo` en `StockApp.Presentation.UiTests` que implementan la interfaz con la firma vieja de 3 parámetros. Si `extension`/`tipoMime` se insertaran ANTES de `ct` (como en la firma que describe la spec en prosa), la llamada existente `GuardarBytesAsync(descarga.Contenido, descarga.NombreArchivo, cts.Token)` pasaría a bindear `cts.Token` (un `CancellationToken`) contra un parámetro `string? extension` -- error de compilación en `MantenimientoViewModel.cs`, y en cada `Setup`/`Verify` de Moq de `MantenimientoViewModelTests.cs` que usa esa misma posición. Poniendo los 2 parámetros nuevos DESPUÉS de `ct` (ambos opcionales, default `null`), las llamadas de 2 y 3 argumentos existentes siguen compilando sin tocarlas, y Moq seguirá matcheándolas correctamente porque el compilador expande los argumentos opcionales omitidos a sus valores default dentro del árbol de expresión de `Setup`/`Verify`. Los únicos archivos que SÍ hay que tocar son los 6 fakes que implementan la interfaz explícitamente con 3 parámetros -- una interfaz exige que la implementación tenga la MISMA cantidad de parámetros, aunque los valores default puedan diferir.

- [ ] **Paso 1: Escribir el test que falla**

Crear `tests/StockApp.Presentation.Tests/Services/ServicioGuardadoArchivoCompatibilidadTests.cs`:

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Guardián de compatibilidad (spec 2026-09-18, export PDF): GuardarBytesAsync ganó dos
/// parámetros nuevos (extension, tipoMime) para que el flujo de PDF pueda pedir un filtro de
/// archivo en el selector nativo de guardado. Los call sites existentes (backups, logs, en
/// MantenimientoViewModel) siguen llamando a la firma de 2/3 argumentos sin tocarse -- este
/// test confirma que, sin pasar extension ni tipoMime, ambos resuelven a null exactamente como
/// antes (ningún DefaultExtension ni FileTypeChoices en el selector).
/// </summary>
public class ServicioGuardadoArchivoCompatibilidadTests
{
    [Fact]
    public async Task GuardarBytesAsync_LlamadoSinExtensionNiTipoMime_ResuelveAmbosComoNull()
    {
        string? extensionRecibida = "no-deberia-quedar-asi";
        string? tipoMimeRecibido = "no-deberia-quedar-asi";

        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<Stream, string, CancellationToken, string?, string?>(
                (_, _, _, extension, tipoMime) =>
                {
                    extensionRecibida = extension;
                    tipoMimeRecibido = tipoMime;
                })
            .ReturnsAsync(true);

        // Llamada "vieja", tal cual la hace MantenimientoViewModel.DescargarLogsAsync: solo
        // stream + nombre, sin extension ni tipoMime. El binding es contra el TIPO de interfaz
        // (guardadoMock.Object es IServicioGuardadoArchivo), así que resuelve los defaults
        // declarados en la interfaz, no en una implementación concreta.
        await guardadoMock.Object.GuardarBytesAsync(new MemoryStream(), "backup_5.dump");

        Assert.Null(extensionRecibida);
        Assert.Null(tipoMimeRecibido);
    }
}
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ServicioGuardadoArchivoCompatibilidadTests"`

Esperado: FALLA en compilación — `IServicioGuardadoArchivo.GuardarBytesAsync` todavía no tiene los parámetros `extension`/`tipoMime` (`CS1501: no overload takes 5 arguments`).

- [ ] **Paso 3: Implementación mínima**

En `src/StockApp.Presentation/Services/IServicioGuardadoArchivo.cs`, reemplazar la firma de `GuardarBytesAsync`:

```csharp
    /// <param name="ct">Token de cancelación — propagado hasta el CopyToAsync final, para que
    /// cancelar la descarga desde la UI (Task 9) corte la copia a disco, no solo la lectura HTTP.</param>
    /// <param name="extension">
    /// Extensión sugerida para el selector nativo (ej. "pdf"), sin punto. Si es <c>null</c>
    /// (default, usado por backups/logs), el selector no ofrece ningún filtro de tipo — mismo
    /// comportamiento que antes de esta firma (spec 2026-09-18, export PDF).
    /// </param>
    /// <param name="tipoMime">Tipo MIME asociado a <paramref name="extension"/> (ej.
    /// "application/pdf"). Se ignora si <paramref name="extension"/> es <c>null</c>.</param>
    /// <returns><c>true</c> si el usuario eligió una ubicación y el archivo se guardó; <c>false</c> si canceló el selector.</returns>
    /// <exception cref="OperationCanceledException">Si <paramref name="ct"/> se cancela durante la copia.</exception>
    Task<bool> GuardarBytesAsync(
        Stream contenido,
        string nombreSugerido,
        CancellationToken ct = default,
        string? extension = null,
        string? tipoMime = null);
```

En `src/StockApp.Presentation/Services/ServicioGuardadoArchivo.cs`, reemplazar `GuardarBytesAsync` y `GuardarBytesInternoAsync`:

```csharp
    /// <inheritdoc />
    public Task<bool> GuardarBytesAsync(
        Stream contenido,
        string nombreSugerido,
        CancellationToken ct = default,
        string? extension = null,
        string? tipoMime = null)
    {
        if (AvaloniaApp.Current is null)
            return Task.FromResult(false);

        return Dispatcher.UIThread.InvokeAsync(
            () => GuardarBytesInternoAsync(contenido, nombreSugerido, ct, extension, tipoMime));
    }

    private static async Task<bool> GuardarBytesInternoAsync(
        Stream contenido, string nombreSugerido, CancellationToken ct, string? extension, string? tipoMime)
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;
        var storageProvider = lifetime?.MainWindow?.StorageProvider;

        if (storageProvider is null)
            return false;

        var opciones = new FilePickerSaveOptions { SuggestedFileName = nombreSugerido };

        // Sin extension no se arma ningún filtro (mismo comportamiento que antes de esta firma:
        // backups/logs guardan cualquier extensión, el selector no debe restringirla).
        if (extension is not null)
        {
            opciones.DefaultExtension = extension;
            opciones.FileTypeChoices = new List<FilePickerFileType>
            {
                new($"Archivo {extension.ToUpperInvariant()}")
                {
                    Patterns = new[] { $"*.{extension}" },
                    MimeTypes = tipoMime is null ? null : new[] { tipoMime },
                },
            };
        }

        var archivo = await storageProvider.SaveFilePickerAsync(opciones);

        if (archivo is null)
            return false;

        await using var destino = await archivo.OpenWriteAsync();
        await contenido.CopyToAsync(destino, ct);

        return true;
    }
```

Actualizar la firma de los 6 fakes (mismo cambio mecánico en cada uno: agregar `, string? extension = null, string? tipoMime = null` a la lista de parámetros, sin tocar el cuerpo):

En `tests/StockApp.Presentation.UiTests/MantenimientoViewTests.cs`, `LibroCajaViewTests.cs`, `AccesoLimitadoViewTests.cs` y `SignoNegativoBadgeTests.cs`:

```csharp
        public Task<bool> GuardarBytesAsync(
            Stream contenido, string nombreSugerido, CancellationToken ct = default,
            string? extension = null, string? tipoMime = null) => Task.FromResult(true);
```

En `tests/StockApp.Presentation.UiTests/GastosViewTests.cs` (usa nombres completos en vez de `using`):

```csharp
        public Task<bool> GuardarBytesAsync(
            System.IO.Stream contenido, string nombreSugerido, System.Threading.CancellationToken ct = default,
            string? extension = null, string? tipoMime = null) => Task.FromResult(true);
```

En `tests/StockApp.Presentation.UiTests/CargaProtegidaEstadoVacioUiTests.cs`:

```csharp
        public Task<bool> GuardarBytesAsync(
            System.IO.Stream contenido, string nombreSugerido, CancellationToken ct = default,
            string? extension = null, string? tipoMime = null)
            => Task.FromResult(false);
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr, en este orden (proyectos distintos, cada uno con su propio build):

```bash
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ServicioGuardadoArchivoCompatibilidadTests"
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~MantenimientoViewModelTests"
dotnet build tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj
```

Esperado: el primer comando PASA (1/1); el segundo PASA igual que antes de este cambio (los call sites de `MantenimientoViewModel.cs` no se tocaron); el tercero compila sin errores (los 6 fakes ya tienen la firma nueva).

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Presentation/Services/IServicioGuardadoArchivo.cs src/StockApp.Presentation/Services/ServicioGuardadoArchivo.cs tests/StockApp.Presentation.Tests/Services/ServicioGuardadoArchivoCompatibilidadTests.cs tests/StockApp.Presentation.UiTests/MantenimientoViewTests.cs tests/StockApp.Presentation.UiTests/LibroCajaViewTests.cs tests/StockApp.Presentation.UiTests/AccesoLimitadoViewTests.cs tests/StockApp.Presentation.UiTests/GastosViewTests.cs tests/StockApp.Presentation.UiTests/CargaProtegidaEstadoVacioUiTests.cs tests/StockApp.Presentation.UiTests/SignoNegativoBadgeTests.cs
git commit -m "feat(export-pdf): expone extension y tipo mime en GuardarBytesAsync sin romper el camino existente"
```

---

### Tarea 9: Helper `ExportacionPdf` + registro de `IPdfExporter` en DI

**Archivos:**
- Crear: `src/StockApp.Presentation/Services/ExportacionPdf.cs`
- Modificar: `src/StockApp.Presentation/App.axaml.cs` (using + registro DI)
- Test: `tests/StockApp.Presentation.Tests/Services/ExportacionPdfTests.cs`

**Interfaces:**
- Consume: `IConfirmacionService.InformarAsync` (ya existe).
- Produce (consumido desde la Tarea 12 en adelante): `static class ExportacionPdf { static Task EjecutarAsync(Func<Task> operacion, IConfirmacionService confirmacion); }`. `IPdfExporter` resuelto por DI como `PdfExporterMigraDoc` (Tarea 3).

- [ ] **Paso 1: Escribir el test que falla**

Crear `tests/StockApp.Presentation.Tests/Services/ExportacionPdfTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using Moq;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Análogo a ExportacionCsv (bugfix 2026-08-14): centraliza el try/catch de guardado a disco
/// para que un fallo DESPUÉS de elegir la ubicación (permiso denegado, disco lleno) se informe
/// en vez de escapar del comando sin observar.
/// </summary>
public class ExportacionPdfTests
{
    [Fact]
    public async Task EjecutarAsync_CaminoFeliz_NoInformaNada()
    {
        var confirmMock = new Mock<IConfirmacionService>();

        await ExportacionPdf.EjecutarAsync(() => Task.CompletedTask, confirmMock.Object);

        confirmMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EjecutarAsync_SiLaOperacionFalla_InformaYNoPropagaLaExcepcion()
    {
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        await ExportacionPdf.EjecutarAsync(
            () => throw new System.IO.IOException("disco lleno"), confirmMock.Object);

        confirmMock.Verify(c => c.InformarAsync("No se pudo guardar el archivo. disco lleno"), Times.Once);
    }
}
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ExportacionPdfTests"`

Esperado: FALLA en compilación — `ExportacionPdf` no existe.

- [ ] **Paso 3: Implementación mínima**

Crear `src/StockApp.Presentation/Services/ExportacionPdf.cs`:

```csharp
using System;
using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Envuelve la escritura a disco de un export PDF, análogo a <see cref="ExportacionCsv"/>
/// (bugfix 2026-08-14): un fallo DESPUÉS de que el usuario ya eligió la ubicación en el
/// selector nativo (permiso denegado, disco lleno, ruta inválida) se informa en vez de escapar
/// de un <c>AsyncRelayCommand</c> sin observar.
/// </summary>
public static class ExportacionPdf
{
    public static async Task EjecutarAsync(Func<Task> operacion, IConfirmacionService confirmacion)
    {
        try
        {
            await operacion();
        }
        catch (Exception ex)
        {
            await confirmacion.InformarAsync($"No se pudo guardar el archivo. {ex.Message}");
        }
    }
}
```

En `src/StockApp.Presentation/App.axaml.cs`, agregar el using junto a `using StockApp.Configuracion;` (línea 12):

```csharp
using StockApp.Configuracion;
using StockApp.Documentos;
```

Y el registro de DI, inmediatamente después de la línea `services.AddTransient<ICsvExporter, CsvExporter>();` (línea 307):

```csharp
        // ── Inc 6: exportación CSV (vive en Application, sin dependencias de Infra — OQ-2)
        services.AddTransient<ICsvExporter, CsvExporter>();

        // ── Export PDF de tablas (spec 2026-09-18): implementación en StockApp.Documentos
        // (MigraDoc) -- la única referencia a esa librería en todo Presentation es esta línea.
        services.AddTransient<IPdfExporter, PdfExporterMigraDoc>();
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ExportacionPdfTests"`

Esperado: PASA (2/2).

Verificar además que el registro de DI no rompe el arranque:

```bash
dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj
```

Esperado: compila sin errores.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Presentation/Services/ExportacionPdf.cs src/StockApp.Presentation/App.axaml.cs tests/StockApp.Presentation.Tests/Services/ExportacionPdfTests.cs
git commit -m "feat(export-pdf): agrega helper ExportacionPdf y registra IPdfExporter en DI"
```

---

### Tarea 10: Aviso de confirmación por volumen (> 500 filas)

**Archivos:**
- Crear: `src/StockApp.Presentation/Services/AvisoVolumenExportacion.cs`
- Test: `tests/StockApp.Presentation.Tests/Services/AvisoVolumenExportacionTests.cs`

**Interfaces:**
- Consume: `IConfirmacionService.PreguntarAsync` (ya existe).
- Produce (consumido desde la Tarea 12 en adelante): `static class AvisoVolumenExportacion { const int UmbralFilas = 500; static Task<bool> ConfirmarAsync(int cantidadFilas, IConfirmacionService confirmacion); }`.

- [ ] **Paso 1: Escribir el test que falla**

Crear `tests/StockApp.Presentation.Tests/Services/AvisoVolumenExportacionTests.cs`:

```csharp
using System.Threading.Tasks;
using Moq;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Sin paginación en el sistema (spec 2026-09-18), exportar una tabla sin filtro de fecha
/// puede generar miles de páginas. Se avisa y se pide confirmación por encima del umbral.
/// </summary>
public class AvisoVolumenExportacionTests
{
    [Fact]
    public async Task ConfirmarAsync_ConExactamenteElUmbral_NoPregunta()
    {
        var confirmMock = new Mock<IConfirmacionService>();

        var resultado = await AvisoVolumenExportacion.ConfirmarAsync(
            AvisoVolumenExportacion.UmbralFilas, confirmMock.Object);

        Assert.True(resultado);
        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmarAsync_PorEncimaDelUmbral_PreguntaConLaCantidadDePaginasEstimadas()
    {
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        var resultado = await AvisoVolumenExportacion.ConfirmarAsync(2000, confirmMock.Object);

        Assert.True(resultado);
        confirmMock.Verify(c => c.PreguntarAsync(It.Is<string>(
            m => m.Contains("2000") && m.Contains("50"))), Times.Once);
    }

    [Fact]
    public async Task ConfirmarAsync_PorEncimaDelUmbral_SiElUsuarioCancela_DevuelveFalse()
    {
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        var resultado = await AvisoVolumenExportacion.ConfirmarAsync(600, confirmMock.Object);

        Assert.False(resultado);
    }
}
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~AvisoVolumenExportacionTests"`

Esperado: FALLA en compilación — `AvisoVolumenExportacion` no existe.

- [ ] **Paso 3: Implementación mínima**

Crear `src/StockApp.Presentation/Services/AvisoVolumenExportacion.cs`:

```csharp
using System;
using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Aviso de volumen antes de exportar a PDF (spec 2026-09-18): sin paginación en el sistema
/// (decisión documentada en DocumentoAdministrativoRepository.cs:42-46), exportar una tabla sin
/// filtro de fecha dentro de unos años puede ser miles de páginas, y alguien le va a dar
/// imprimir. Se avisa y se pide confirmación por encima de <see cref="UmbralFilas"/>.
/// </summary>
public static class AvisoVolumenExportacion
{
    public const int UmbralFilas = 500;
    private const int FilasPorPaginaEstimadas = 40;

    /// <summary>
    /// Si <paramref name="cantidadFilas"/> supera <see cref="UmbralFilas"/>, pregunta al
    /// usuario confirmando la cantidad aproximada de páginas. Si no lo supera, confirma
    /// automáticamente sin interrumpir (caso común).
    /// </summary>
    public static async Task<bool> ConfirmarAsync(int cantidadFilas, IConfirmacionService confirmacion)
    {
        if (cantidadFilas <= UmbralFilas)
            return true;

        var paginasEstimadas = (int)Math.Ceiling(cantidadFilas / (double)FilasPorPaginaEstimadas);
        return await confirmacion.PreguntarAsync(
            $"Esta exportación tiene {cantidadFilas} filas y va a generar aproximadamente " +
            $"{paginasEstimadas} páginas. ¿Confirma continuar?");
    }
}
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~AvisoVolumenExportacionTests"`

Esperado: PASA (3/3).

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Presentation/Services/AvisoVolumenExportacion.cs tests/StockApp.Presentation.Tests/Services/AvisoVolumenExportacionTests.cs
git commit -m "feat(export-pdf): agrega aviso de confirmacion por volumen antes de exportar"
```

---

### Tarea 11: Apertura del PDF tras guardarlo

**Archivos:**
- Modificar: `src/StockApp.Presentation/Services/ExportacionPdf.cs` (agrega `OfrecerAbrirAsync`)
- Test: `tests/StockApp.Presentation.Tests/Services/ExportacionPdfTests.cs` (agrega 2 tests)

**Interfaces:**
- Consume: `IServicioAperturaArchivo.AbrirAsync(string, byte[])` (ya existe, usado hoy por `AdjuntosPanelViewModel`).
- Produce (consumido desde la Tarea 12 en adelante): `static Task ExportacionPdf.OfrecerAbrirAsync(byte[] contenido, string nombreArchivo, IConfirmacionService confirmacion, IServicioAperturaArchivo apertura)`.

**Por qué se ofrece abrir en vez de imprimir directo:** Avalonia no tiene API de diálogo de impresión (no hay equivalente a `PrintDialog`/`PrintManager`; el feature request es AvaloniaUI/Avalonia#12567, sin asignar desde agosto 2023). Reutilizando `IServicioAperturaArchivo` (ya usado para adjuntos), el usuario obtiene la ventana de impresión COMPLETA del sistema operativo desde el visor de PDF que se abra.

- [ ] **Paso 1: Escribir el test que falla**

Agregar a `tests/StockApp.Presentation.Tests/Services/ExportacionPdfTests.cs`:

```csharp
    [Fact]
    public async Task OfrecerAbrirAsync_SiElUsuarioConfirma_AbreElArchivo()
    {
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        var aperturaMock = new Mock<IServicioAperturaArchivo>();
        var contenido = new byte[] { 1, 2, 3 };

        await ExportacionPdf.OfrecerAbrirAsync(contenido, "valorizacion.pdf", confirmMock.Object, aperturaMock.Object);

        aperturaMock.Verify(a => a.AbrirAsync("valorizacion.pdf", contenido), Times.Once);
    }

    [Fact]
    public async Task OfrecerAbrirAsync_SiElUsuarioCancela_NoAbreNada()
    {
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);
        var aperturaMock = new Mock<IServicioAperturaArchivo>();

        await ExportacionPdf.OfrecerAbrirAsync(new byte[] { 1 }, "valorizacion.pdf", confirmMock.Object, aperturaMock.Object);

        aperturaMock.Verify(a => a.AbrirAsync(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~OfrecerAbrirAsync"`

Esperado: FALLA en compilación — `ExportacionPdf.OfrecerAbrirAsync` no existe.

- [ ] **Paso 3: Implementación mínima**

En `src/StockApp.Presentation/Services/ExportacionPdf.cs`, agregar el método (y el `using` de `IServicioAperturaArchivo`, ya en el mismo namespace `StockApp.Presentation.Services` — no hace falta using extra):

```csharp
    /// <summary>
    /// Ofrece abrir el PDF recién guardado con el visor del sistema, reusando
    /// <see cref="IServicioAperturaArchivo"/> (ya usado para adjuntos): escribe a un temporal
    /// propio y dispara <c>Process.Start(UseShellExecute = true)</c> -- desde el visor el
    /// usuario obtiene la ventana de impresión completa del SO (Avalonia no tiene diálogo de
    /// impresión propio, ver AvaloniaUI/Avalonia#12567).
    /// </summary>
    public static async Task OfrecerAbrirAsync(
        byte[] contenido, string nombreArchivo,
        IConfirmacionService confirmacion, IServicioAperturaArchivo apertura)
    {
        var abrir = await confirmacion.PreguntarAsync("PDF guardado. ¿Desea abrirlo ahora?");
        if (!abrir)
            return;

        await apertura.AbrirAsync(nombreArchivo, contenido);
    }
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ExportacionPdfTests"`

Esperado: PASA (4/4— los 2 de la Tarea 9 más los 2 de esta tarea).

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Presentation/Services/ExportacionPdf.cs tests/StockApp.Presentation.Tests/Services/ExportacionPdfTests.cs
git commit -m "feat(export-pdf): ofrece abrir el pdf con el visor del sistema tras guardarlo"
```

---

### Tarea 12: Valorización — primera pantalla completa (plantilla de referencia)

Esta tarea es la PLANTILLA DE REFERENCIA para las Tareas 13-20: botón en el XAML + constante `ColumnasPdf` + comando `ExportarPdfAsync` en el VM + test. 6 columnas de grilla → A4 vertical (regla de la Tarea 4). Sin filtros propios (la pantalla no tiene filtros, solo Buscar).

**Archivos:**
- Modificar: `src/StockApp.Presentation/ViewModels/Reportes/ValorizacionViewModel.cs`
- Modificar: `src/StockApp.Presentation/Views/Reportes/ValorizacionView.axaml`
- Modificar: `tests/StockApp.Presentation.Tests/ViewModels/Reportes/ValorizacionViewModelTests.cs`

**Interfaces:**
- Consume: `IPdfExporter` (Tarea 3/9), `IServicioAperturaArchivo` (existente), `ICurrentSession` (existente, `src/StockApp.Application/Interfaces/ICurrentSession.cs`), `ExportacionPdf.EjecutarAsync`/`OfrecerAbrirAsync` (Tareas 9/11), `AvisoVolumenExportacion.ConfirmarAsync` (Tarea 10), `IServicioGuardadoArchivo.GuardarBytesAsync` con `extension`/`tipoMime` (Tarea 8).
- Produce: patrón replicado tal cual (adaptando título/columnas/filtros) en las Tareas 13-20.

- [ ] **Paso 1: Escribir el test que falla**

Modificar el helper `Crear` de `tests/StockApp.Presentation.Tests/ViewModels/Reportes/ValorizacionViewModelTests.cs` para agregar los 3 mocks nuevos, y agregar los tests de PDF. Reemplazar el helper existente:

```csharp
    private static (
        ValorizacionViewModel vm,
        Mock<IReporteStockService> servicioMock,
        Mock<ICsvExporter> exporterMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<ICurrentSession> sessionMock)
        Crear(
            IReadOnlyList<ValorizacionItemDto>? items = null,
            ValorizacionTotalesDto? totales = null)
    {
        var servicioMock = new Mock<IReporteStockService>();
        var exporterMock = new Mock<ICsvExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var confirmMock = new Mock<IConfirmacionService>();
        var pdfExporterMock = new Mock<IPdfExporter>();
        var aperturaMock = new Mock<IServicioAperturaArchivo>();
        var sessionMock = new Mock<ICurrentSession>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        servicioMock
            .Setup(s => s.ObtenerValorizacionAsync())
            .ReturnsAsync(new ValorizacionReporteDto(
                items ?? new List<ValorizacionItemDto>(),
                totales ?? new ValorizacionTotalesDto(0m)));

        var vm = new ValorizacionViewModel(
            servicioMock.Object, exporterMock.Object, guardadoMock.Object, confirmMock.Object,
            pdfExporterMock.Object, aperturaMock.Object, sessionMock.Object);
        return (vm, servicioMock, exporterMock, guardadoMock, confirmMock, pdfExporterMock, aperturaMock, sessionMock);
    }
```

Actualizar las líneas `var (vm, servicioMock, _, _, _) = Crear();` y similares (hay 3 usos con 5 elementos en la tupla) a 8 elementos con descartes (`var (vm, servicioMock, _, _, _, _, _, _) = Crear();`), y las 3 líneas `var (vm, _, exporterMock, guardadoMock, _) = Crear(items);` a `var (vm, _, exporterMock, guardadoMock, _, _, _, _) = Crear(items);`. Agregar `using StockApp.Application.Interfaces;` y `using StockApp.Application.Auth;` y `using StockApp.Domain.Enums;` al inicio del archivo si faltan (para `ICurrentSession`, `UsuarioSesion`, `RolUsuario`).

Agregar al final de la clase:

```csharp
    // ── Export PDF (spec 2026-09-18) ────────────────────────────────────────

    [Fact]
    public async Task ExportarPdfCommand_LlamaAlExportadorConLasColumnasDeLaGrilla()
    {
        var items = new List<ValorizacionItemDto> { CrearItem(1) };
        var (vm, _, _, guardadoMock, _, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ValorizacionItemDto>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "valorizacion.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            vm.Items,
            It.Is<IReadOnlyList<string>>(cols => cols.SequenceEqual(ValorizacionViewModel.ColumnasPdf)),
            It.IsAny<MetadatosDocumento>()),
            Times.Once);
        guardadoMock.Verify(g => g.GuardarBytesAsync(
            It.IsAny<Stream>(), "valorizacion.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinItems_NoExporta()
    {
        var (vm, _, _, _, _, pdfExporterMock, _, _) = Crear(new List<ValorizacionItemDto>());

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_ConMuchasFilas_PreguntaAntesDeExportar()
    {
        var items = Enumerable.Range(1, 600).Select(CrearItem).ToList();
        var (vm, _, _, guardadoMock, confirmMock, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_SiFallaGuardarBytesAsync_InformaYNoPropagaLaExcepcion()
    {
        var items = new List<ValorizacionItemDto> { CrearItem(1) };
        var (vm, _, _, guardadoMock, confirmMock, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("disco lleno"));

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync("No se pudo guardar el archivo. disco lleno"), Times.Once);
    }
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ValorizacionViewModelTests"`

Esperado: FALLA en compilación — `ValorizacionViewModel.ExportarPdfCommand`/`ColumnasPdf` no existen, y el constructor no acepta 7 parámetros.

- [ ] **Paso 3: Implementación mínima**

En `src/StockApp.Presentation/ViewModels/Reportes/ValorizacionViewModel.cs`, agregar usings:

```csharp
using System.IO;
using System.Threading;
using StockApp.Application.Interfaces;
```

Agregar la constante `ColumnasPdf` junto a `ColumnOrder`:

```csharp
    /// <summary>
    /// Orden EXACTO de columnas para el export PDF (spec 2026-09-18): las de la GRILLA, no las
    /// del CSV -- excluye ProductoId (ID interno de Postgres, no significa nada en papel).
    /// Deliberadamente separada de ColumnOrder: un cambio futuro en el CSV no debe alterar el PDF.
    /// </summary>
    public static readonly IReadOnlyList<string> ColumnasPdf = new[]
    {
        "Codigo", "Nombre", "Categoria", "StockActual", "PrecioCosto", "ValorCosto",
    };
```

Agregar las 3 dependencias nuevas al campo/constructor:

```csharp
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    public ValorizacionViewModel(
        IReporteStockService servicio,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion,
        IPdfExporter pdfExporter,
        IServicioAperturaArchivo apertura,
        ICurrentSession session)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _guardado = guardado;
        _confirmacion = confirmacion;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;
    }
```

Agregar el comando, después de `ExportarAsync`:

```csharp
    /// <summary>
    /// Exporta <see cref="Items"/> a PDF con las columnas de la grilla, membrete institucional
    /// y aviso de volumen si supera 500 filas (spec 2026-09-18). No hace nada si no hay datos
    /// cargados. Ofrece abrir el PDF con el visor del sistema tras guardarlo.
    /// </summary>
    [RelayCommand]
    private async Task ExportarPdfAsync()
    {
        if (Items.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(Items.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Valorización de inventario",
                DescripcionFiltros: "Sin filtros aplicados.",
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(Items, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, "valorizacion.pdf", extension: "pdf", tipoMime: "application/pdf");

            if (guardado)
                await ExportacionPdf.OfrecerAbrirAsync(pdf, "valorizacion.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
```

En `src/StockApp.Presentation/Views/Reportes/ValorizacionView.axaml`, agregar el botón junto al de CSV:

```xml
                <Button Classes="secondary" Content="Exportar CSV" Command="{Binding ExportarCommand}" />
                <Button Classes="secondary" Content="Exportar PDF" Command="{Binding ExportarPdfCommand}" />
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ValorizacionViewModelTests"`

Esperado: PASA (todos, los de CSV existentes más los 4 de PDF nuevos). Además, porque se tocó un `x:DataType` view y su VM (RSC-equivalente de este proyecto: compiled bindings fallan en build, no en test):

```bash
dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj
```

Esperado: compila sin errores (un typo de binding en una vista con `x:DataType` es error de build, no null silencioso).

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Reportes/ValorizacionViewModel.cs src/StockApp.Presentation/Views/Reportes/ValorizacionView.axaml tests/StockApp.Presentation.Tests/ViewModels/Reportes/ValorizacionViewModelTests.cs
git commit -m "feat(export-pdf): agrega exportar a pdf en Valorizacion de inventario"
```

---

### Tarea 13: Stock por categoría (4 columnas, A4 vertical)

**Archivos:**
- Modificar: `src/StockApp.Presentation/ViewModels/Reportes/StockCategoriaViewModel.cs`
- Modificar: `src/StockApp.Presentation/Views/Reportes/StockCategoriaView.axaml`
- Modificar: `tests/StockApp.Presentation.Tests/ViewModels/Reportes/StockCategoriaViewModelTests.cs`

**Interfaces:**
- Consume: mismas de la Tarea 12 (`IPdfExporter`, `IServicioAperturaArchivo`, `ICurrentSession`, `ExportacionPdf`, `AvisoVolumenExportacion`).
- Produce: `StockCategoriaViewModel.ColumnasPdf`, `ExportarPdfCommand`.

- [ ] **Paso 1: Escribir el test que falla**

El helper real de `StockCategoriaViewModelTests.cs` (líneas 25-45) devuelve `(StockCategoriaViewModel vm, Mock<IReporteStockService> servicioMock, Mock<ICsvExporter> exporterMock, Mock<IServicioGuardadoArchivo> guardadoMock, Mock<IConfirmacionService> confirmMock)` y construye el VM con 4 argumentos. Reemplazar por:

```csharp
    private static (
        StockCategoriaViewModel vm,
        Mock<IReporteStockService> servicioMock,
        Mock<ICsvExporter> exporterMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<ICurrentSession> sessionMock)
        Crear(IReadOnlyList<StockCategoriaDto>? items = null)
    {
        var servicioMock = new Mock<IReporteStockService>();
        var exporterMock = new Mock<ICsvExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var confirmMock = new Mock<IConfirmacionService>();
        var pdfExporterMock = new Mock<IPdfExporter>();
        var aperturaMock = new Mock<IServicioAperturaArchivo>();
        var sessionMock = new Mock<ICurrentSession>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        servicioMock
            .Setup(s => s.ObtenerStockPorCategoriaAsync())
            .ReturnsAsync(items ?? new List<StockCategoriaDto>());

        var vm = new StockCategoriaViewModel(
            servicioMock.Object, exporterMock.Object, guardadoMock.Object, confirmMock.Object,
            pdfExporterMock.Object, aperturaMock.Object, sessionMock.Object);
        return (vm, servicioMock, exporterMock, guardadoMock, confirmMock, pdfExporterMock, aperturaMock, sessionMock);
    }
```

Actualizar todos los call sites existentes de `Crear(...)` agregando tres descartes más al final (`_, _, _`). Agregar `using StockApp.Application.Auth;`, `using StockApp.Application.Interfaces;`, `using StockApp.Domain.Enums;` al inicio del archivo si faltan, y agregar:

```csharp
    [Fact]
    public async Task ExportarPdfCommand_LlamaAlExportadorConLasColumnasDeLaGrilla()
    {
        var items = new List<StockCategoriaDto> { new("Almacén", 10, 100m, 500m) };
        var (vm, _, _, guardadoMock, _, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<StockCategoriaDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "stock-categoria.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            vm.Items,
            It.Is<IReadOnlyList<string>>(cols => cols.SequenceEqual(StockCategoriaViewModel.ColumnasPdf)),
            It.IsAny<MetadatosDocumento>()), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinItems_NoExporta()
    {
        var (vm, _, _, _, _, pdfExporterMock, _, _) = Crear(new List<StockCategoriaDto>());

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<StockCategoriaDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()), Times.Never);
    }
```

(Nota: constructor real de `StockCategoriaDto` es `record StockCategoriaDto(string Categoria, int CantidadProductos, decimal StockTotal, decimal ValorCosto)`, mismo orden que `ColumnOrder`.)

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~StockCategoriaViewModelTests"`

Esperado: FALLA en compilación.

- [ ] **Paso 3: Implementación mínima**

En `src/StockApp.Presentation/ViewModels/Reportes/StockCategoriaViewModel.cs`, agregar usings (`System.IO`, `System.Threading`, `StockApp.Application.Interfaces`), la constante:

```csharp
    /// <summary>Columnas de la grilla para PDF (spec 2026-09-18) — coinciden con ColumnOrder,
    /// pero se mantienen separadas a propósito (ver Restricciones globales del plan).</summary>
    public static readonly IReadOnlyList<string> ColumnasPdf = new[]
    {
        "Categoria", "CantidadProductos", "StockTotal", "ValorCosto",
    };
```

los 3 campos, el constructor ampliado y el comando:

```csharp
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    public StockCategoriaViewModel(
        IReporteStockService servicio,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion,
        IPdfExporter pdfExporter,
        IServicioAperturaArchivo apertura,
        ICurrentSession session)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _guardado = guardado;
        _confirmacion = confirmacion;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;
    }

    [RelayCommand]
    private async Task ExportarPdfAsync()
    {
        if (Items.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(Items.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Stock por categoría",
                DescripcionFiltros: "Sin filtros aplicados.",
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(Items, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, "stock-categoria.pdf", extension: "pdf", tipoMime: "application/pdf");

            if (guardado)
                await ExportacionPdf.OfrecerAbrirAsync(pdf, "stock-categoria.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
```

En `src/StockApp.Presentation/Views/Reportes/StockCategoriaView.axaml`:

```xml
                <Button Classes="secondary" Content="Exportar CSV" Command="{Binding ExportarCommand}" />
                <Button Classes="secondary" Content="Exportar PDF" Command="{Binding ExportarPdfCommand}" />
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~StockCategoriaViewModelTests"` y `dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj`.

Esperado: ambos PASAN.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Reportes/StockCategoriaViewModel.cs src/StockApp.Presentation/Views/Reportes/StockCategoriaView.axaml tests/StockApp.Presentation.Tests/ViewModels/Reportes/StockCategoriaViewModelTests.cs
git commit -m "feat(export-pdf): agrega exportar a pdf en Stock por categoria"
```

---

### Tarea 14: Más movidos (4 columnas, A4 vertical, con filtros de fecha/top)

**Archivos:**
- Modificar: `src/StockApp.Presentation/ViewModels/Reportes/MasMovidosViewModel.cs`
- Modificar: `src/StockApp.Presentation/Views/Reportes/MasMovidosView.axaml`
- Modificar: `tests/StockApp.Presentation.Tests/ViewModels/Reportes/MasMovidosViewModelTests.cs`

**Interfaces:**
- Consume: mismas de la Tarea 12.
- Produce: `MasMovidosViewModel.ColumnasPdf`, `ExportarPdfCommand`.

- [ ] **Paso 1: Escribir el test que falla**

El helper real de `MasMovidosViewModelTests.cs` (líneas 26-46) devuelve `(MasMovidosViewModel vm, Mock<IReporteStockService> servicioMock, Mock<ICsvExporter> exporterMock, Mock<IServicioGuardadoArchivo> guardadoMock, Mock<IConfirmacionService> confirmMock)` y construye el VM con 4 argumentos. Reemplazar por:

```csharp
    private static (
        MasMovidosViewModel vm,
        Mock<IReporteStockService> servicioMock,
        Mock<ICsvExporter> exporterMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<ICurrentSession> sessionMock)
        Crear(IReadOnlyList<MasMovidoDto>? items = null)
    {
        var servicioMock = new Mock<IReporteStockService>();
        var exporterMock = new Mock<ICsvExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var confirmMock = new Mock<IConfirmacionService>();
        var pdfExporterMock = new Mock<IPdfExporter>();
        var aperturaMock = new Mock<IServicioAperturaArchivo>();
        var sessionMock = new Mock<ICurrentSession>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        servicioMock
            .Setup(s => s.ObtenerMasMovidosAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(items ?? new List<MasMovidoDto>());

        var vm = new MasMovidosViewModel(
            servicioMock.Object, exporterMock.Object, guardadoMock.Object, confirmMock.Object,
            pdfExporterMock.Object, aperturaMock.Object, sessionMock.Object);
        return (vm, servicioMock, exporterMock, guardadoMock, confirmMock, pdfExporterMock, aperturaMock, sessionMock);
    }
```

Actualizar todos los call sites existentes de `Crear(...)` agregando tres descartes más al final (`_, _, _`). Agregar `using StockApp.Application.Auth;`, `using StockApp.Application.Interfaces;`, `using StockApp.Domain.Enums;` al inicio del archivo si faltan, y agregar:

```csharp
    [Fact]
    public async Task ExportarPdfCommand_LaDescripcionDeFiltrosIncluyeElPeriodoYElTopN()
    {
        var items = new List<MasMovidoDto> { new(1, "P001", "Azúcar", 5, 20m) };
        var (vm, _, _, guardadoMock, _, pdfExporterMock, _, _) = Crear(items);
        vm.FechaDesde = new DateTime(2026, 1, 1);
        vm.FechaHasta = new DateTime(2026, 1, 31);
        await vm.BuscarCommand.ExecuteAsync(null);
        MetadatosDocumento? metadatosCapturados = null;
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<MasMovidoDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Callback<IEnumerable<MasMovidoDto>, IReadOnlyList<string>, MetadatosDocumento>((_, _, m) => metadatosCapturados = m)
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "mas-movidos.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.NotNull(metadatosCapturados);
        Assert.Contains("01/01/2026", metadatosCapturados!.DescripcionFiltros);
        Assert.Contains("31/01/2026", metadatosCapturados.DescripcionFiltros);
        Assert.Contains(vm.TopN.ToString(), metadatosCapturados.DescripcionFiltros);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinItems_NoExporta()
    {
        var (vm, _, _, _, _, pdfExporterMock, _, _) = Crear(new List<MasMovidoDto>());

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MasMovidoDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()), Times.Never);
    }
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~MasMovidosViewModelTests"`

Esperado: FALLA en compilación.

- [ ] **Paso 3: Implementación mínima**

En `src/StockApp.Presentation/ViewModels/Reportes/MasMovidosViewModel.cs`, agregar la constante, los 3 campos, el constructor ampliado y el resto:

```csharp
    public static readonly IReadOnlyList<string> ColumnasPdf = new[]
    {
        "Codigo", "Nombre", "CantidadMovimientos", "VolumenTotal",
    };

    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    public MasMovidosViewModel(
        IReporteStockService servicio,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion,
        IPdfExporter pdfExporter,
        IServicioAperturaArchivo apertura,
        ICurrentSession session)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _guardado = guardado;
        _confirmacion = confirmacion;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;
    }

    private string ConstruirDescripcionFiltros()
    {
        var periodo = FechaDesde is null && FechaHasta is null
            ? "Todo el histórico"
            : $"Período: {FechaDesde?.ToString("dd/MM/yyyy") ?? "(sin desde)"} a {FechaHasta?.ToString("dd/MM/yyyy") ?? "(sin hasta)"}";
        return $"{periodo}. Top {TopN}.";
    }

    [RelayCommand]
    private async Task ExportarPdfAsync()
    {
        if (Items.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(Items.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Productos más movidos",
                DescripcionFiltros: ConstruirDescripcionFiltros(),
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(Items, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, "mas-movidos.pdf", extension: "pdf", tipoMime: "application/pdf");

            if (guardado)
                await ExportacionPdf.OfrecerAbrirAsync(pdf, "mas-movidos.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
```

En `src/StockApp.Presentation/Views/Reportes/MasMovidosView.axaml`:

```xml
                <Button Classes="secondary" Content="Exportar CSV" Command="{Binding ExportarCommand}" />
                <Button Classes="secondary" Content="Exportar PDF" Command="{Binding ExportarPdfCommand}" />
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~MasMovidosViewModelTests"` y `dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj`.

Esperado: ambos PASAN.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Reportes/MasMovidosViewModel.cs src/StockApp.Presentation/Views/Reportes/MasMovidosView.axaml tests/StockApp.Presentation.Tests/ViewModels/Reportes/MasMovidosViewModelTests.cs
git commit -m "feat(export-pdf): agrega exportar a pdf en Mas movidos"
```

---

### Tarea 15: Control POA (6 columnas, A4 vertical)

`ControlPoaViewModel` no tiene `ICurrentSession` inyectado hoy — se agrega en esta tarea.

**Archivos:**
- Modificar: `src/StockApp.Presentation/ViewModels/Finanzas/ControlPoaViewModel.cs`
- Modificar: `src/StockApp.Presentation/Views/Finanzas/ControlPoaView.axaml`
- Modificar: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/ControlPoaViewModelTests.cs`
- Modificar: `tests/StockApp.Presentation.UiTests/*` que instancien `ControlPoaViewModel` directo (buscar con `grep -rn "new ControlPoaViewModel" tests/` antes de tocar los tests de VM — si hay UiTests que la instancian, agregarles el mismo mock/fake que a los otros 6 de la Tarea 8).

**Interfaces:**
- Consume: mismas de la Tarea 12, más `ICurrentSession` como dependencia NUEVA de este VM (antes no la tenía).
- Produce: `ControlPoaViewModel.ColumnasPdf`, `ExportarPdfCommand`.

- [ ] **Paso 1: Escribir el test que falla**

El helper real de `ControlPoaViewModelTests.cs` (líneas 15-36) devuelve la tupla `(ControlPoaViewModel vm, Mock<IFinanzasVistasService> svcMock, Mock<INavigationService> navMock, Mock<ICsvExporter> csvMock, Mock<IServicioGuardadoArchivo> guardadoMock, Mock<IConfirmacionService> confirmMock)` y construye el VM con `new ControlPoaViewModel(svc.Object, nav.Object, csv.Object, guardado.Object, confirm.Object)`. Reemplazar por:

```csharp
    private static (
        ControlPoaViewModel vm,
        Mock<IFinanzasVistasService> svcMock,
        Mock<INavigationService> navMock,
        Mock<ICsvExporter> csvMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<ICurrentSession> sessionMock)
        Crear(IReadOnlyList<ControlPoaLineaDto>? lineas = null)
    {
        var svc = new Mock<IFinanzasVistasService>();
        svc.Setup(s => s.ObtenerControlPoaAsync(It.IsAny<int>())).ReturnsAsync(lineas ?? new List<ControlPoaLineaDto>());
        var nav = new Mock<INavigationService>();
        var csv = new Mock<ICsvExporter>();
        csv.Setup(c => c.Exportar(It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv");
        var guardado = new Mock<IServicioGuardadoArchivo>();
        guardado.Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var pdfExporter = new Mock<IPdfExporter>();
        var apertura = new Mock<IServicioAperturaArchivo>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        var vm = new ControlPoaViewModel(
            svc.Object, nav.Object, csv.Object, guardado.Object, confirm.Object, pdfExporter.Object, apertura.Object, session.Object);
        return (vm, svc, nav, csv, guardado, confirm, pdfExporter, apertura, session);
    }
```

Actualizar todos los call sites existentes de `Crear(...)` agregando tres descartes más al final (`_, _, _`). Agregar `using StockApp.Application.Auth;`, `using StockApp.Application.Interfaces;`, `using StockApp.Domain.Enums;` al inicio del archivo si faltan.

Agregar el test:

```csharp
    [Fact]
    public async Task ExportarPdfCommand_LlamaAlExportadorConLasColumnasDeLaGrilla()
    {
        var items = new List<ControlPoaLineaDto> { new(1, "Obras viales", "Vialidad", 2026, 100000m, 40000m, 60000m, 0.4m, false) };
        var (vm, _, _, _, guardadoMock, _, pdfExporterMock, _, _) = Crear(items);
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            vm.Filas,
            It.Is<IReadOnlyList<string>>(cols => cols.SequenceEqual(ControlPoaViewModel.ColumnasPdf)),
            It.IsAny<MetadatosDocumento>()), Times.Once);
    }
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ControlPoaViewModelTests"`

Esperado: FALLA en compilación.

- [ ] **Paso 3: Implementación mínima**

En `src/StockApp.Presentation/ViewModels/Finanzas/ControlPoaViewModel.cs`, agregar `using System.IO;`, `using System.Threading;`, `using StockApp.Application.Interfaces;` (si falta), la constante (6 de las 8 columnas de `ColumnasCsv`, excluye `Ejercicio` y `Sobregirada` — spec: "sobra en el CSV"), las 3 dependencias y el comando:

```csharp
    public static readonly IReadOnlyList<string> ColumnasPdf = new[]
    {
        "Nombre", "Programa", "Presupuesto", "Gastado", "Saldo", "PorcentajeEjecucion",
    };

    [RelayCommand]
    private async Task ExportarPdfAsync()
    {
        if (Filas.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(Filas.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Control POA",
                DescripcionFiltros: $"Ejercicio: {Ejercicio}.",
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(Filas, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, $"control-poa-{Ejercicio}.pdf", extension: "pdf", tipoMime: "application/pdf");

            if (guardado)
                await ExportacionPdf.OfrecerAbrirAsync(pdf, $"control-poa-{Ejercicio}.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
```

Agregar los 3 campos y ampliar el constructor:

```csharp
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    public ControlPoaViewModel(
        IFinanzasVistasService service, INavigationService navigation,
        ICsvExporter csvExporter, IServicioGuardadoArchivo guardado, IConfirmacionService confirmacion,
        IPdfExporter pdfExporter, IServicioAperturaArchivo apertura, ICurrentSession session)
    {
        _service     = service;
        _navigation  = navigation;
        _csvExporter = csvExporter;
        _guardado    = guardado;
        _confirmacion = confirmacion;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;

        FilasView = new DataGridCollectionView(Filas);
    }
```

En `src/StockApp.Presentation/Views/Finanzas/ControlPoaView.axaml`:

```xml
                <Button Classes="secondary" Content="Exportar CSV" Command="{Binding ExportarCsvCommand}" />
                <Button Classes="secondary" Content="Exportar PDF" Command="{Binding ExportarPdfCommand}" />
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ControlPoaViewModelTests"` y `dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj`.

Esperado: ambos PASAN. Si algún `UiTests` instanciaba `ControlPoaViewModel` directo, correr también `dotnet build tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj` y agregar los mocks/fakes que falten.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/ControlPoaViewModel.cs src/StockApp.Presentation/Views/Finanzas/ControlPoaView.axaml tests/StockApp.Presentation.Tests/ViewModels/Finanzas/ControlPoaViewModelTests.cs
git commit -m "feat(export-pdf): agrega exportar a pdf en Control POA"
```

---

### Tarea 16: Auditoría (6 columnas, A4 vertical, caso de texto libre ilimitado en Detalle)

**Archivos:**
- Modificar: `src/StockApp.Presentation/ViewModels/Reportes/AuditoriaLogViewModel.cs`
- Modificar: `src/StockApp.Presentation/Views/Reportes/AuditoriaLogView.axaml`
- Modificar: `tests/StockApp.Presentation.Tests/ViewModels/Reportes/AuditoriaLogViewModelTests.cs`

**Interfaces:**
- Consume: mismas de la Tarea 12, más `ICurrentSession` NUEVA.
- Produce: `AuditoriaLogViewModel.ColumnasPdf`, `ExportarPdfCommand`.

- [ ] **Paso 1: Escribir el test que falla**

El helper real de `AuditoriaLogViewModelTests.cs` (líneas 34-63) devuelve `(AuditoriaLogViewModel vm, Mock<IAuditoriaQueryService> servicioMock, Mock<ICsvExporter> exporterMock, Mock<IServicioGuardadoArchivo> guardadoMock, Mock<IConfirmacionService> confirmMock, Mock<IUsuarioService> usuarioSvcMock)` y construye el VM con 5 argumentos. Reemplazar por:

```csharp
    private static (
        AuditoriaLogViewModel vm,
        Mock<IAuditoriaQueryService> servicioMock,
        Mock<ICsvExporter> exporterMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<IUsuarioService> usuarioSvcMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<ICurrentSession> sessionMock)
        Crear(IReadOnlyList<AuditoriaItemDto>? items = null, IReadOnlyList<UsuarioDto>? usuarios = null)
    {
        var servicioMock = new Mock<IAuditoriaQueryService>();
        var exporterMock = new Mock<ICsvExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var confirmMock = new Mock<IConfirmacionService>();
        var usuarioSvcMock = new Mock<IUsuarioService>();
        var pdfExporterMock = new Mock<IPdfExporter>();
        var aperturaMock = new Mock<IServicioAperturaArchivo>();
        var sessionMock = new Mock<ICurrentSession>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        servicioMock
            .Setup(s => s.ObtenerLogAsync(
                It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(items ?? new List<AuditoriaItemDto>());

        usuarioSvcMock
            .Setup(s => s.ListarAsync())
            .ReturnsAsync(usuarios ?? new List<UsuarioDto>());

        var vm = new AuditoriaLogViewModel(
            servicioMock.Object, exporterMock.Object, guardadoMock.Object, confirmMock.Object,
            usuarioSvcMock.Object, pdfExporterMock.Object, aperturaMock.Object, sessionMock.Object);
        return (vm, servicioMock, exporterMock, guardadoMock, confirmMock, usuarioSvcMock, pdfExporterMock, aperturaMock, sessionMock);
    }
```

Actualizar todos los call sites existentes de `Crear(...)` agregando tres descartes más al final (`_, _, _`). Agregar `using StockApp.Application.Interfaces;` si falta (`UsuarioSesion`/`RolUsuario` ya se importan hoy vía `StockApp.Application.Auth`/`StockApp.Domain.Enums`).

Agregar el test:

```csharp
    [Fact]
    public async Task ExportarPdfCommand_ConDetalleMuyLargo_LoExportaCompleto()
    {
        var detalleLargo = string.Join(" ", Enumerable.Range(1, 50).Select(i => $"campo{i}=valor{i}"));
        var items = new List<AuditoriaItemDto>
        {
            new(DateTime.UtcNow, "admin", AccionAuditada.CambioPrecio, "Producto", 1, detalleLargo),
        };
        var (vm, _, _, guardadoMock, _, _, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<AuditoriaItemDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "auditoria.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        // El texto libre sin tope de Detalle es el caso extremo de la regla de wrap (Tarea 5):
        // PlantillaTabular lo recibe entero, sin que el ViewModel lo recorte antes.
        pdfExporterMock.Verify(e => e.Exportar(
            It.Is<IEnumerable<AuditoriaItemDto>>(coleccion => coleccion.Single().Detalle == detalleLargo),
            It.Is<IReadOnlyList<string>>(cols => cols.SequenceEqual(AuditoriaLogViewModel.ColumnasPdf)),
            It.IsAny<MetadatosDocumento>()), Times.Once);
    }
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~AuditoriaLogViewModelTests"`

Esperado: FALLA en compilación.

- [ ] **Paso 3: Implementación mínima**

En `src/StockApp.Presentation/ViewModels/Reportes/AuditoriaLogViewModel.cs`:

```csharp
    public static readonly IReadOnlyList<string> ColumnasPdf = new[]
    {
        "Fecha", "NombreUsuario", "Accion", "Entidad", "EntidadId", "Detalle",
    };

    private string ConstruirDescripcionFiltros()
    {
        var usuario = UsuarioFiltroSeleccionado?.Valor is null ? "Todos" : UsuarioFiltroSeleccionado.Nombre;
        var periodo = FechaDesde is null && FechaHasta is null
            ? "Todo el histórico"
            : $"{FechaDesde?.ToString("dd/MM/yyyy") ?? "(sin desde)"} a {FechaHasta?.ToString("dd/MM/yyyy") ?? "(sin hasta)"}";
        return $"Usuario: {usuario}. Período: {periodo}.";
    }

    [RelayCommand]
    private async Task ExportarPdfAsync()
    {
        if (Items.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(Items.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Log de auditoría",
                DescripcionFiltros: ConstruirDescripcionFiltros(),
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(Items, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, "auditoria.pdf", extension: "pdf", tipoMime: "application/pdf");

            if (guardado)
                await ExportacionPdf.OfrecerAbrirAsync(pdf, "auditoria.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
```

Agregar los 3 campos y ampliar el constructor:

```csharp
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    public AuditoriaLogViewModel(
        IAuditoriaQueryService servicio,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion,
        IUsuarioService usuarioService,
        IPdfExporter pdfExporter,
        IServicioAperturaArchivo apertura,
        ICurrentSession session)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _guardado = guardado;
        _confirmacion = confirmacion;
        _usuarioService = usuarioService;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;
    }
```

En `src/StockApp.Presentation/Views/Reportes/AuditoriaLogView.axaml`:

```xml
                <Button Classes="secondary" Content="Exportar CSV" Command="{Binding ExportarCommand}" />
                <Button Classes="secondary" Content="Exportar PDF" Command="{Binding ExportarPdfCommand}" />
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~AuditoriaLogViewModelTests"` y `dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj`.

Esperado: ambos PASAN.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Reportes/AuditoriaLogViewModel.cs src/StockApp.Presentation/Views/Reportes/AuditoriaLogView.axaml tests/StockApp.Presentation.Tests/ViewModels/Reportes/AuditoriaLogViewModelTests.cs
git commit -m "feat(export-pdf): agrega exportar a pdf en Log de auditoria"
```

---

### Tarea 17: Historial por producto (8 columnas → A4 apaisado)

Primera pantalla que dispara la regla de la Tarea 4 (más de 6 columnas). El orden de columnas del PDF sigue el orden de la GRILLA (`Fecha, Tipo, Motivo, Cantidad, PrecioUnitario, StockAnterior, StockNuevo, Comentario`), que difiere del orden de `ColumnOrder` (CSV, 12 columnas con IDs internos).

**Archivos:**
- Modificar: `src/StockApp.Presentation/ViewModels/Reportes/HistorialPorProductoViewModel.cs`
- Modificar: `src/StockApp.Presentation/Views/Reportes/HistorialPorProductoView.axaml`
- Modificar: `tests/StockApp.Presentation.Tests/ViewModels/Reportes/HistorialPorProductoViewModelTests.cs`

**Interfaces:**
- Consume: mismas de la Tarea 12, más `ICurrentSession` NUEVA.
- Produce: `HistorialPorProductoViewModel.ColumnasPdf`, `ExportarPdfCommand`.

- [ ] **Paso 1: Escribir el test que falla**

El helper real de `HistorialPorProductoViewModelTests.cs` (líneas 45-67) devuelve `(HistorialPorProductoViewModel vm, Mock<IReporteStockService> servicioMock, Mock<ICsvExporter> exporterMock, Mock<IServicioGuardadoArchivo> guardadoMock, Mock<IConfirmacionService> confirmMock, Mock<IProductoService> productoSvcMock)` y construye el VM con 5 argumentos. Reemplazar por:

```csharp
    private static (
        HistorialPorProductoViewModel vm,
        Mock<IReporteStockService> servicioMock,
        Mock<ICsvExporter> exporterMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<IProductoService> productoSvcMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<ICurrentSession> sessionMock)
        Crear(IReadOnlyList<MovimientoHistorialDto>? items = null)
    {
        var servicioMock = new Mock<IReporteStockService>();
        var exporterMock = new Mock<ICsvExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var confirmMock = new Mock<IConfirmacionService>();
        var productoSvcMock = new Mock<IProductoService>();
        var pdfExporterMock = new Mock<IPdfExporter>();
        var aperturaMock = new Mock<IServicioAperturaArchivo>();
        var sessionMock = new Mock<ICurrentSession>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        servicioMock
            .Setup(s => s.ObtenerHistorialPorProductoAsync(
                It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(items ?? new List<MovimientoHistorialDto>());

        var vm = new HistorialPorProductoViewModel(
            servicioMock.Object, exporterMock.Object, guardadoMock.Object, confirmMock.Object,
            productoSvcMock.Object, pdfExporterMock.Object, aperturaMock.Object, sessionMock.Object);
        return (vm, servicioMock, exporterMock, guardadoMock, confirmMock, productoSvcMock, pdfExporterMock, aperturaMock, sessionMock);
    }
```

Actualizar todos los call sites existentes de `Crear(...)` agregando tres descartes más al final (`_, _, _`). Agregar `using StockApp.Application.Auth;`, `using StockApp.Application.Interfaces;` si faltan.

Agregar el test (nota: `MovimientoHistorialDto` tiene 13 campos posicionales — el último es `UsuarioNombre`, ver `src/StockApp.Application/Movimientos/Dtos.cs:34-46`: `record MovimientoHistorialDto(int MovimientoId, int ProductoId, string ProductoNombre, TipoMovimiento Tipo, MotivoMovimiento Motivo, decimal Cantidad, decimal PrecioUnitario, decimal StockAnterior, decimal StockNuevo, string? Comentario, DateTime Fecha, int UsuarioId, string UsuarioNombre)`):

```csharp
    [Fact]
    public async Task ExportarPdfCommand_UsaElOrdenDeColumnasDeLaGrilla_NoElDelCsv()
    {
        var items = new List<MovimientoHistorialDto>
        {
            new(1, 10, "Azúcar", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 5m, 100m, 10m, 15m, "ok", DateTime.UtcNow, 1, "Admin"),
        };
        var (vm, _, _, guardadoMock, _, _, pdfExporterMock, _, _) = Crear(items);
        vm.ProductoId = 10;
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<MovimientoHistorialDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "historial-producto.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MovimientoHistorialDto>>(),
            new[] { "Fecha", "Tipo", "Motivo", "Cantidad", "PrecioUnitario", "StockAnterior", "StockNuevo", "Comentario" },
            It.IsAny<MetadatosDocumento>()), Times.Once);
        Assert.Equal(8, HistorialPorProductoViewModel.ColumnasPdf.Count);
    }
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~HistorialPorProductoViewModelTests"`

Esperado: FALLA en compilación.

- [ ] **Paso 3: Implementación mínima**

En `src/StockApp.Presentation/ViewModels/Reportes/HistorialPorProductoViewModel.cs`:

```csharp
    /// <summary>Orden de la GRILLA (no del CSV, que tiene 12 columnas con IDs internos) —
    /// 8 columnas dispara apaisado automático (Tarea 4 de la plantilla).</summary>
    public static readonly IReadOnlyList<string> ColumnasPdf = new[]
    {
        "Fecha", "Tipo", "Motivo", "Cantidad", "PrecioUnitario", "StockAnterior", "StockNuevo", "Comentario",
    };

    private string ConstruirDescripcionFiltros()
    {
        var producto = ProductoSeleccionado is null
            ? "(sin producto)"
            : $"{ProductoSeleccionado.Codigo} - {ProductoSeleccionado.Nombre}";
        var periodo = FechaDesde is null && FechaHasta is null
            ? "Todo el histórico"
            : $"{FechaDesde?.ToString("dd/MM/yyyy") ?? "(sin desde)"} a {FechaHasta?.ToString("dd/MM/yyyy") ?? "(sin hasta)"}";
        return $"Producto: {producto}. Período: {periodo}.";
    }

    [RelayCommand]
    private async Task ExportarPdfAsync()
    {
        if (Items.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(Items.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Historial por producto",
                DescripcionFiltros: ConstruirDescripcionFiltros(),
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(Items, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, "historial-producto.pdf", extension: "pdf", tipoMime: "application/pdf");

            if (guardado)
                await ExportacionPdf.OfrecerAbrirAsync(pdf, "historial-producto.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
```

Agregar los 3 campos y ampliar el constructor:

```csharp
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    public HistorialPorProductoViewModel(
        IReporteStockService servicio,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion,
        IProductoService productoService,
        IPdfExporter pdfExporter,
        IServicioAperturaArchivo apertura,
        ICurrentSession session)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _guardado = guardado;
        _confirmacion = confirmacion;
        _productoService = productoService;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;

        BuscarProductosAsync = BuscarProductosInternalAsync;
    }
```

En `src/StockApp.Presentation/Views/Reportes/HistorialPorProductoView.axaml`:

```xml
                <Button Classes="secondary" Content="Exportar CSV" Command="{Binding ExportarCommand}" />
                <Button Classes="secondary" Content="Exportar PDF" Command="{Binding ExportarPdfCommand}" />
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~HistorialPorProductoViewModelTests"` y `dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj`.

Esperado: ambos PASAN.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Reportes/HistorialPorProductoViewModel.cs src/StockApp.Presentation/Views/Reportes/HistorialPorProductoView.axaml tests/StockApp.Presentation.Tests/ViewModels/Reportes/HistorialPorProductoViewModelTests.cs
git commit -m "feat(export-pdf): agrega exportar a pdf en Historial por producto"
```

---

### Tarea 18: Libro Caja (10 columnas → A4 apaisado, botón condicionado a `!VerAnioCompleto`)

`LibroCajaViewModel` no tiene `ICurrentSession` inyectado hoy — se agrega en esta tarea. El botón PDF respeta la misma condición que el de CSV: no se ofrece exportar cuando la vista está en modo "año completo" (`MovimientosView`/`Movimientos` quedan vacíos en ese modo, ver `LibroCajaViewModel.CargarAsync`).

**Archivos:**
- Modificar: `src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs`
- Modificar: `src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml`
- Modificar: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/LibroCajaViewModelTests.cs`
- Modificar: `tests/StockApp.Presentation.UiTests/LibroCajaViewTests.cs` (el fake de `IServicioGuardadoArchivo` ya se actualizó en la Tarea 8; verificar si el archivo instancia `LibroCajaViewModel` directo y, de ser así, agregarle los 3 fakes/mocks nuevos)

**Interfaces:**
- Consume: mismas de la Tarea 12, más `ICurrentSession` NUEVA.
- Produce: `LibroCajaViewModel.ColumnasPdf`, `ExportarPdfCommand`.

- [ ] **Paso 1: Escribir el test que falla**

El helper real de `LibroCajaViewModelTests.cs` (líneas 14-31) devuelve `(LibroCajaViewModel vm, Mock<IFinanzasVistasService> svcMock, Mock<IServicioGuardadoArchivo> guardadoMock, Mock<IConfirmacionService> confirmMock)` y construye el VM con `new LibroCajaViewModel(svc.Object, csv.Object, guardado.Object, confirm.Object)` (el mock de `ICsvExporter` se crea inline y no se devuelve en la tupla). Reemplazar por:

```csharp
    private static (
        LibroCajaViewModel vm,
        Mock<IFinanzasVistasService> svcMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<ICurrentSession> sessionMock)
        Crear()
    {
        var svc = new Mock<IFinanzasVistasService>();
        var csv = new Mock<ICsvExporter>();
        csv.Setup(c => c.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv");
        var guardado = new Mock<IServicioGuardadoArchivo>();
        guardado.Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var pdfExporter = new Mock<IPdfExporter>();
        var apertura = new Mock<IServicioAperturaArchivo>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        var vm = new LibroCajaViewModel(
            svc.Object, csv.Object, guardado.Object, confirm.Object, pdfExporter.Object, apertura.Object, session.Object);
        return (vm, svc, guardado, confirm, pdfExporter, apertura, session);
    }
```

Actualizar todos los call sites existentes de `Crear()` agregando tres descartes más al final (`_, _, _`). Agregar `using StockApp.Application.Auth;`, `using StockApp.Application.Interfaces;`, `using StockApp.Domain.Enums;` si faltan.

Agregar los tests:

```csharp
    [Fact]
    public async Task ExportarPdfCommand_ConAnioCompleto_NoExporta()
    {
        var (vm, _, _, _, pdfExporterMock, _, _) = Crear();
        vm.VerAnioCompleto = true;
        await vm.CargarAsync();

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_ConMes_UsaLasDiezColumnasDeLaGrilla()
    {
        var (vm, servicioMock, guardadoMock, _, pdfExporterMock, _, _) = Crear();
        // Ajustar el Setup de servicioMock.ObtenerLibroCajaMesAsync para devolver al menos un
        // MovimientoCajaDto, siguiendo el mismo patrón que los tests de CargarAsync existentes.
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MovimientoCajaDto>>(),
            It.Is<IReadOnlyList<string>>(cols => cols.SequenceEqual(LibroCajaViewModel.ColumnasPdf) && cols.Count == 10),
            It.IsAny<MetadatosDocumento>()), Times.Once);
    }
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~LibroCajaViewModelTests"`

Esperado: FALLA en compilación.

- [ ] **Paso 3: Implementación mínima**

En `src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs`:

```csharp
    public static readonly IReadOnlyList<string> ColumnasPdf = new[]
    {
        "Fecha", "Tipo", "Concepto", "ProveedorNombre", "NumeroFactura",
        "FuenteNombre", "RubroNombre", "Ingreso", "Egreso", "SaldoCorrido",
    };

    [RelayCommand]
    private async Task ExportarPdfAsync()
    {
        if (VerAnioCompleto || Movimientos.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(Movimientos.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Libro caja",
                DescripcionFiltros: $"Mes: {Mes:00}/{Anio:0000}.",
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(Movimientos, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var nombreArchivo = $"libro-caja-{Anio:0000}-{Mes:00}.pdf";
            var guardado = await _guardado.GuardarBytesAsync(
                stream, nombreArchivo, extension: "pdf", tipoMime: "application/pdf");

            if (guardado)
                await ExportacionPdf.OfrecerAbrirAsync(pdf, nombreArchivo, _confirmacion, _apertura);
        }, _confirmacion);
    }
```

Agregar los 3 campos y ampliar el constructor:

```csharp
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    public LibroCajaViewModel(
        IFinanzasVistasService service, ICsvExporter csvExporter, IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion, IPdfExporter pdfExporter, IServicioAperturaArchivo apertura,
        ICurrentSession session)
    {
        _service     = service;
        _csvExporter = csvExporter;
        _guardado    = guardado;
        _confirmacion = confirmacion;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;

        MovimientosView = new DataGridCollectionView(Movimientos);
    }
```

En `src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml`:

```xml
                <Button Classes="secondary" Content="Exportar CSV" Command="{Binding ExportarCsvCommand}" IsVisible="{Binding !VerAnioCompleto}" />
                <Button Classes="secondary" Content="Exportar PDF" Command="{Binding ExportarPdfCommand}" IsVisible="{Binding !VerAnioCompleto}" />
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~LibroCajaViewModelTests"` y `dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj` y `dotnet build tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`.

Esperado: los tres PASAN.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml tests/StockApp.Presentation.Tests/ViewModels/Finanzas/LibroCajaViewModelTests.cs
git commit -m "feat(export-pdf): agrega exportar a pdf en Libro caja"
```

---

### Tarea 19: Gastos (11 columnas → A4 apaisado, peor caso: 4 columnas de texto largo, filtros combinables)

**Archivos:**
- Modificar: `src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs`
- Modificar: `src/StockApp.Presentation/Views/Finanzas/GastosView.axaml`
- Modificar: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastosViewModelTests.cs`

**Interfaces:**
- Consume: mismas de la Tarea 12. `GastosViewModel` YA tiene `ICurrentSession _session` (la usa para `PuedeRegistrarPagos`/`PuedeRegistrarGastos`) — no se agrega de nuevo, se reutiliza.
- Produce: `GastosViewModel.ColumnasPdf`, `ExportarPdfCommand`.

- [ ] **Paso 1: Escribir el test que falla**

El helper real de `GastosViewModelTests.cs` (líneas 50-93) es `Crear(...)`, devuelve la tupla `(GastosViewModel vm, Mock<IGastoService> svcMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock, Mock<IServicioGuardadoArchivo> guardadoMock)` y construye `GastosViewModel` con 10 argumentos posicionales. Además del helper, hay DOS construcciones directas de `GastosViewModel` con los mismos 10 argumentos, sin pasar por `Crear` (`CargarAsync_ConsultaProveedoresActivos_NoTodosLosProveedores` y `CargarAsync_SiProveedoresLanzaUnauthorized_NoPropagaLaExcepcionYNoDuplicaAviso`). Las tres hay que actualizarlas.

Reemplazar la firma y el cuerpo de `Crear` para agregar los 2 mocks nuevos al final de la tupla y del constructor:

```csharp
    private static (GastosViewModel vm,
                    Mock<IGastoService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock,
                    Mock<IServicioGuardadoArchivo> guardadoMock,
                    Mock<IPdfExporter> pdfExporterMock,
                    Mock<IServicioAperturaArchivo> aperturaMock)
        Crear(
            IReadOnlyList<Gasto>? gastos = null, IReadOnlyList<LineaPoa>? lineasPoa = null,
            RolUsuario rol = RolUsuario.Admin, IEnumerable<string>? permisos = null)
    {
        // ... (cuerpo sin cambios hasta la línea de "var guardado = ...") ...
        var guardado = new Mock<IServicioGuardadoArchivo>();
        guardado.Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var pdfExporter = new Mock<IPdfExporter>();
        var apertura = new Mock<IServicioAperturaArchivo>();

        var vm = new GastosViewModel(
            svc.Object, session.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object, csv.Object, guardado.Object, pdfExporter.Object, apertura.Object);
        return (vm, svc, nav, confirm, guardado, pdfExporter, apertura);
    }
```

Actualizar todos los call sites de `Crear(...)` existentes (`var (vm, _, _, _, _) = Crear(...)`, `var (vm, svc, _, _, _) = Crear()`, etc.) agregando dos descartes más al final (`_, _`). En las 2 construcciones directas (líneas ~623-625 y ~667-669), agregar `new Mock<IPdfExporter>().Object, new Mock<IServicioAperturaArchivo>().Object` al final de la lista de argumentos de `new GastosViewModel(...)`.

Agregar los tests nuevos, usando los 2 mocks ya devueltos por `Crear`:

```csharp
    [Fact]
    public async Task ExportarPdfCommand_LaDescripcionDeFiltrosSoloIncluyeLosFiltrosActivos()
    {
        var (vm, _, _, _, guardadoMock, pdfExporterMock, _) = Crear();
        vm.FechaDesde = new DateTime(2026, 1, 1);
        vm.FechaHasta = new DateTime(2026, 1, 31);
        await vm.CargarAsync();
        MetadatosDocumento? metadatosCapturados = null;
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<GastoFila>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Callback<IEnumerable<GastoFila>, IReadOnlyList<string>, MetadatosDocumento>((_, _, m) => metadatosCapturados = m)
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.NotNull(metadatosCapturados);
        Assert.Contains("01/01/2026", metadatosCapturados!.DescripcionFiltros);
        Assert.Contains("31/01/2026", metadatosCapturados.DescripcionFiltros);
        // Sin proveedor/fuente/rubro/línea/estado seleccionados, esas etiquetas NO aparecen.
        Assert.DoesNotContain("Proveedor:", metadatosCapturados.DescripcionFiltros);
    }

    [Fact]
    public async Task ExportarPdfCommand_UsaLasOnceColumnasDeLaGrilla()
    {
        var (vm, _, _, _, guardadoMock, pdfExporterMock, _) = Crear();
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<GastoFila>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<GastoFila>>(),
            It.Is<IReadOnlyList<string>>(cols => cols.SequenceEqual(GastosViewModel.ColumnasPdf) && cols.Count == 11),
            It.IsAny<MetadatosDocumento>()), Times.Once);
    }
```

`ICurrentSession` NO se agrega: `GastosViewModel` ya lo recibe hoy (lo usa para `PuedeRegistrarPagos`/`PuedeRegistrarGastos`), así que `_session` se reutiliza tal cual en el comando nuevo.

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~GastosViewModelTests"`

Esperado: FALLA en compilación.

- [ ] **Paso 3: Implementación mínima**

En `src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs`, agregar junto a `ColumnasCsv`:

```csharp
    private static readonly IReadOnlyList<string> ColumnasPdf = new[]
    {
        nameof(GastoFila.Fecha), nameof(GastoFila.ProveedorNombre), nameof(GastoFila.NumeroFactura),
        nameof(GastoFila.Detalle), nameof(GastoFila.FuenteNombre), nameof(GastoFila.RubroNombre),
        nameof(GastoFila.LineaPoaNombre), nameof(GastoFila.MontoTotal), nameof(GastoFila.TotalPagado),
        nameof(GastoFila.Saldo), nameof(GastoFila.Estado),
    };
```

Nota: en Gastos, `ColumnasPdf` coincide en contenido con `ColumnasCsv` (la grilla y el CSV tienen las mismas 11 columnas, ver spec) pero se declara como constante SEPARADA a propósito — si el día de mañana el CSV cambia, el PDF no debe cambiar solo.

Agregar el helper de descripción de filtros y el comando:

```csharp
    private string ConstruirDescripcionFiltros()
    {
        var partes = new List<string>();
        if (FechaDesde is not null || FechaHasta is not null)
            partes.Add($"Período: {FechaDesde?.ToString("dd/MM/yyyy") ?? "(sin desde)"} a {FechaHasta?.ToString("dd/MM/yyyy") ?? "(sin hasta)"}");
        if (ProveedorSeleccionado is not null)
            partes.Add($"Proveedor: {ProveedorSeleccionado.Nombre}");
        if (FuenteSeleccionada is not null)
            partes.Add($"Fuente: {FuenteSeleccionada.Nombre}");
        if (RubroSeleccionado is not null)
            partes.Add($"Rubro: {RubroSeleccionado.Nombre}");
        if (LineaPoaSeleccionada is not null)
            partes.Add($"Línea POA: {LineaPoaSeleccionada.Nombre}");
        if (EstadoSeleccionado != EstadoTodos)
            partes.Add($"Estado: {EstadoSeleccionado}");

        return partes.Count == 0 ? "Sin filtros aplicados." : string.Join(" | ", partes) + ".";
    }

    /// <summary>
    /// El guardado a disco corre bajo <see cref="ExportacionPdf"/> (análogo a
    /// <see cref="ExportacionCsv"/>, bugfix 2026-08-14). Peor caso de la spec 2026-09-18: 11
    /// columnas (apaisado automático, Tarea 4) con 4 columnas de texto libre potencialmente
    /// largo (Detalle, Proveedor, Fuente, Rubro, Línea POA) que se envuelven sin truncar (Tarea 5).
    /// </summary>
    [RelayCommand]
    private async Task ExportarPdfAsync()
    {
        if (Filas.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(Filas.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Gastos y facturas",
                DescripcionFiltros: ConstruirDescripcionFiltros(),
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(Filas, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var nombreArchivo = $"gastos-{DateTime.Now:yyyyMMdd}.pdf";
            var guardado = await _guardado.GuardarBytesAsync(
                stream, nombreArchivo, extension: "pdf", tipoMime: "application/pdf");

            if (guardado)
                await ExportacionPdf.OfrecerAbrirAsync(pdf, nombreArchivo, _confirmacion, _apertura);
        }, _confirmacion);
    }
```

Agregar los 2 campos nuevos y ampliar el constructor (`_session` ya existe hoy — no se toca):

```csharp
    private readonly IPdfExporter             _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;

    public GastosViewModel(
        IGastoService service,
        ICurrentSession session,
        IProveedorService proveedoresService,
        IFuenteFinanciamientoService fuentesService,
        IRubroGastoService rubrosService,
        ILineaPoaService lineasService,
        INavigationService navigation,
        IConfirmacionService confirmacion,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado,
        IPdfExporter pdfExporter,
        IServicioAperturaArchivo apertura)
    {
        _service            = service;
        _session            = session;
        _proveedoresService = proveedoresService;
        _fuentesService     = fuentesService;
        _rubrosService      = rubrosService;
        _lineasService      = lineasService;
        _navigation         = navigation;
        _confirmacion       = confirmacion;
        _csvExporter        = csvExporter;
        _guardado           = guardado;
        _pdfExporter        = pdfExporter;
        _apertura           = apertura;

        FilasView = new DataGridCollectionView(Filas);
    }
```

En `src/StockApp.Presentation/Views/Finanzas/GastosView.axaml`, agregar junto al botón de CSV (mismo patrón de icono que el existente, reutilizando `xmlns:i="https://github.com/projektanker/icons.avalonia"` ya declarado en el header del archivo):

```xml
                <Button Classes="secondary" Command="{Binding ExportarCsvCommand}">
                    <StackPanel Orientation="Horizontal" Spacing="6">
                        <i:Icon Value="mdi-file-export" />
                        <TextBlock Text="Exportar CSV" VerticalAlignment="Center" />
                    </StackPanel>
                </Button>
                <Button Classes="secondary" Command="{Binding ExportarPdfCommand}">
                    <StackPanel Orientation="Horizontal" Spacing="6">
                        <i:Icon Value="mdi-file-pdf-box" />
                        <TextBlock Text="Exportar PDF" VerticalAlignment="Center" />
                    </StackPanel>
                </Button>
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~GastosViewModelTests"` y `dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj` y `dotnet build tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj` (por `GastosViewTests.cs`, ya tocado en la Tarea 8).

Esperado: los tres PASAN.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs src/StockApp.Presentation/Views/Finanzas/GastosView.axaml tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastosViewModelTests.cs
git commit -m "feat(export-pdf): agrega exportar a pdf en Gastos y facturas"
```

---

### Tarea 20: Reporte de tareas — agrega los DOS botones (CSV y PDF)

Única pantalla del alcance que hoy NO tiene ninguna exportación. `ReporteTareasViewModel` solo recibe `IReporteTareasService` hoy — esta tarea le agrega 6 dependencias nuevas de una vez (`ICsvExporter`, `IPdfExporter`, `IServicioGuardadoArchivo`, `IServicioAperturaArchivo`, `IConfirmacionService`, `ICurrentSession`), así que rompe la firma del constructor para TODO el que lo instancie directo.

**Archivos:**
- Modificar: `src/StockApp.Application/Reportes/TareasReporteDtos.cs` (sin cambios de contrato — solo referencia, `FilaReporteTareas`/`ReporteTareasDto` ya existen)
- Modificar: `src/StockApp.Presentation/ViewModels/Reportes/ReporteTareasViewModel.cs`
- Modificar: `src/StockApp.Presentation/Views/Reportes/ReporteTareasView.axaml`
- Modificar: `tests/StockApp.Presentation.Tests/ViewModels/Reportes/ReporteTareasViewModelTests.cs`
- Modificar: `tests/StockApp.Presentation.UiTests/ReporteTareasCriterioCierreAvisoTests.cs` (el `Montar()` helper instancia `new ReporteTareasViewModel(new ReporteTareasServiceFake())` — hay que pasarle los 6 fakes/dobles nuevos)

**Interfaces:**
- Consume: `ICsvExporter` (existente), `IPdfExporter`/`ExportacionPdf`/`AvisoVolumenExportacion` (Tareas 2-11), `IServicioGuardadoArchivo`, `IServicioAperturaArchivo`, `IConfirmacionService`, `ICurrentSession` (todas existentes).
- Produce: `ReporteTareasViewModel.ColumnasCsv`/`ColumnasPdf`, `ExportarCsvCommand`/`ExportarPdfCommand`.

- [ ] **Paso 1: Escribir el test que falla**

En `tests/StockApp.Presentation.Tests/ViewModels/Reportes/ReporteTareasViewModelTests.cs`, reemplazar el helper `Crear`:

```csharp
    private static (
        ReporteTareasViewModel vm,
        Mock<IReporteTareasService> servicioMock,
        Mock<ICsvExporter> csvExporterMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<ICurrentSession> sessionMock)
        Crear(ReporteTareasDto? respuesta = null)
    {
        var servicioMock = new Mock<IReporteTareasService>();
        servicioMock
            .Setup(s => s.ObtenerAsync(It.IsAny<FiltroReporteTareas>()))
            .ReturnsAsync(respuesta ?? new ReporteTareasDto(new List<FilaReporteTareas>(), 0));

        var csvExporterMock = new Mock<ICsvExporter>();
        var pdfExporterMock = new Mock<IPdfExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var aperturaMock = new Mock<IServicioAperturaArchivo>();
        var confirmMock = new Mock<IConfirmacionService>();
        var sessionMock = new Mock<ICurrentSession>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        var vm = new ReporteTareasViewModel(
            servicioMock.Object, csvExporterMock.Object, pdfExporterMock.Object,
            guardadoMock.Object, aperturaMock.Object, confirmMock.Object, sessionMock.Object);
        return (vm, servicioMock, csvExporterMock, pdfExporterMock, guardadoMock, aperturaMock, confirmMock, sessionMock);
    }
```

Actualizar todos los `var (vm, _) = Crear();` existentes a `var (vm, _, _, _, _, _, _, _) = Crear();` (y el único con respuesta, `var (vm, _) = Crear(dto);`, a `var (vm, _, _, _, _, _, _, _) = Crear(dto);`). Agregar `using StockApp.Application.Exportacion;`, `using StockApp.Application.Interfaces;`, `using StockApp.Application.Auth;`, `using StockApp.Domain.Enums;`, `using StockApp.Presentation.Services;` si faltan.

Agregar al final de la clase:

```csharp
    // ── Export (spec 2026-09-18): única pantalla del alcance sin ninguna exportación previa ──

    private static FilaReporteTareas Fila(string clasificador) => new(clasificador, 1, 0, 2, 0, 3);

    [Fact]
    public async Task ExportarCsvCommand_UsaLasColumnasFijasDeLaMatriz()
    {
        var dto = new ReporteTareasDto(new List<FilaReporteTareas> { Fila("Centro") }, 3);
        var (vm, _, csvExporterMock, _, guardadoMock, _, _, _) = Crear(dto);
        await vm.CargarAsync();
        csvExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<FilaReporteTareas>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv");
        guardadoMock.Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        await vm.ExportarCsvCommand.ExecuteAsync(null);

        csvExporterMock.Verify(e => e.Exportar(
            vm.Items,
            new[] { "Clasificador", "Pendientes", "EnCurso", "Terminadas", "Canceladas", "Total" }),
            Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_UsaLasColumnasFijasDeLaMatriz()
    {
        var dto = new ReporteTareasDto(new List<FilaReporteTareas> { Fila("Centro") }, 3);
        var (vm, _, _, pdfExporterMock, guardadoMock, _, _, _) = Crear(dto);
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<FilaReporteTareas>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            vm.Items,
            It.Is<IReadOnlyList<string>>(cols => cols.SequenceEqual(ReporteTareasViewModel.ColumnasPdf)),
            It.IsAny<MetadatosDocumento>()), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinItems_NoExporta()
    {
        var (vm, _, _, pdfExporterMock, _, _, _, _) = Crear();

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<FilaReporteTareas>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()), Times.Never);
    }
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Correr: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ReporteTareasViewModelTests"`

Esperado: FALLA en compilación — el constructor de `ReporteTareasViewModel` solo acepta 1 parámetro hoy.

- [ ] **Paso 3: Implementación mínima**

En `src/StockApp.Presentation/ViewModels/Reportes/ReporteTareasViewModel.cs`, agregar usings (`System.IO`, `System.Threading`, `StockApp.Application.Exportacion`, `StockApp.Application.Interfaces`, `StockApp.Presentation.Services`), los campos y el constructor ampliado:

```csharp
    /// <summary>Columnas fijas de la matriz (spec 2026-09-18): el agrupador cambia filas y
    /// contenido, nunca las columnas. Mismo orden para CSV y PDF -- a diferencia de las otras
    /// 8 pantallas, acá coinciden exactamente porque no hay IDs internos que excluir.</summary>
    public static readonly IReadOnlyList<string> ColumnasCsv = new[]
    {
        nameof(FilaReporteTareas.Clasificador), nameof(FilaReporteTareas.Pendientes),
        nameof(FilaReporteTareas.EnCurso), nameof(FilaReporteTareas.Terminadas),
        nameof(FilaReporteTareas.Canceladas), nameof(FilaReporteTareas.Total),
    };

    public static readonly IReadOnlyList<string> ColumnasPdf = ColumnasCsv;

    private readonly ICsvExporter _csvExporter;
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly IConfirmacionService _confirmacion;
    private readonly ICurrentSession _session;

    public ReporteTareasViewModel(
        IReporteTareasService servicio,
        ICsvExporter csvExporter,
        IPdfExporter pdfExporter,
        IServicioGuardadoArchivo guardado,
        IServicioAperturaArchivo apertura,
        IConfirmacionService confirmacion,
        ICurrentSession session)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _pdfExporter = pdfExporter;
        _guardado = guardado;
        _apertura = apertura;
        _confirmacion = confirmacion;
        _session = session;
        _agrupadorSeleccionado = AgrupadoresDisponibles[0];
        _criterioSeleccionado = CriterioFechaTareas.Creacion;

        var anioActual = DateTime.Now.Year;
        _fechaDesde = new DateTime(anioActual, 1, 1);
        _fechaHasta = new DateTime(anioActual, 12, 31);
    }
```

(El cuerpo del constructor viejo ya asignaba `_agrupadorSeleccionado`/`_criterioSeleccionado`/fechas por defecto — se conserva tal cual, solo se agregan las 6 asignaciones nuevas antes.)

Agregar los comandos, después de `CargarAsync`:

```csharp
    private string ConstruirDescripcionFiltros() =>
        $"Agrupado por: {AgrupadorSeleccionado.Nombre}. " +
        $"Criterio: {(EsCriterioCreacion ? "Fecha de creación" : "Fecha de cierre")}. " +
        $"Período: {FechaDesde:dd/MM/yyyy} a {FechaHasta:dd/MM/yyyy}.";

    [RelayCommand]
    private async Task ExportarCsvAsync()
    {
        if (Items.Count == 0)
            return;

        await ExportacionCsv.EjecutarAsync(async () =>
        {
            var csv = _csvExporter.Exportar(Items, ColumnasCsv);
            await _guardado.GuardarTextoAsync(csv, "reporte-tareas.csv");
        }, _confirmacion);
    }

    [RelayCommand]
    private async Task ExportarPdfAsync()
    {
        if (Items.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(Items.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Estadística de tareas",
                DescripcionFiltros: ConstruirDescripcionFiltros(),
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(Items, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, "reporte-tareas.pdf", extension: "pdf", tipoMime: "application/pdf");

            if (guardado)
                await ExportacionPdf.OfrecerAbrirAsync(pdf, "reporte-tareas.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
```

En `src/StockApp.Presentation/Views/Reportes/ReporteTareasView.axaml`, agregar ambos botones junto a "Buscar":

```xml
                <Button Classes="primary" Content="Buscar" Command="{Binding BuscarCommand}" />
                <Button Classes="secondary" Content="Exportar CSV" Command="{Binding ExportarCsvCommand}" />
                <Button Classes="secondary" Content="Exportar PDF" Command="{Binding ExportarPdfCommand}" />
```

En `tests/StockApp.Presentation.UiTests/ReporteTareasCriterioCierreAvisoTests.cs`, el método `Montar()` construye `new ReporteTareasViewModel(new ReporteTareasServiceFake())`. Agregar fakes mínimos no-op para los 6 parámetros nuevos (este proyecto no referencia Moq, ver comentario existente en el archivo), agregar los usings que falten al inicio del archivo (`using System.IO;`, `using System.Threading;`, `using StockApp.Application.Auth;`, `using StockApp.Application.Exportacion;`, `using StockApp.Application.Interfaces;`, `using StockApp.Domain.Entities;`, `using StockApp.Domain.Enums;`, `using StockApp.Presentation.Services;`) y actualizar la llamada:

```csharp
    private sealed class CsvExporterFake : ICsvExporter
    {
        public string Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnOrder) => string.Empty;
    }

    private sealed class PdfExporterFake : IPdfExporter
    {
        public byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnas, MetadatosDocumento metadatos) => Array.Empty<byte>();
    }

    private sealed class GuardadoFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarTextoAsync(string contenido, string nombreSugerido) => Task.FromResult(false);
        public Task<bool> GuardarBytesAsync(
            Stream contenido, string nombreSugerido, CancellationToken ct = default,
            string? extension = null, string? tipoMime = null) => Task.FromResult(false);
    }

    private sealed class AperturaFake : IServicioAperturaArchivo
    {
        public Task AbrirAsync(string nombreArchivo, byte[] contenido) => Task.CompletedTask;
    }

    private sealed class ConfirmacionFake : IConfirmacionService
    {
        public Task<bool> PreguntarAsync(string mensaje) => Task.FromResult(false);
        public Task InformarAsync(string mensaje) => Task.CompletedTask;
        public Task<string?> PedirTextoAsync(string titulo, string mensaje) => Task.FromResult<string?>(null);
    }

    private sealed class SessionFake : ICurrentSession
    {
        public bool EstaAutenticado => true;
        public UsuarioSesion? UsuarioActual => new(1, "admin", RolUsuario.Admin, null);
        public RolUsuario? RolActual => RolUsuario.Admin;
        public IReadOnlySet<string> PermisosActuales => new HashSet<string>();
        public void EstablecerPermisos(IReadOnlySet<string> permisos) { }
        public void IniciarSesion(Usuario usuario) { }
        public void CerrarSesion() { }
    }

    private static (Window Window, ReporteTareasViewModel Vm) Montar()
    {
        var vm = new ReporteTareasViewModel(
            new ReporteTareasServiceFake(), new CsvExporterFake(), new PdfExporterFake(),
            new GuardadoFake(), new AperturaFake(), new ConfirmacionFake(), new SessionFake());
        var vista = new ReporteTareasView();
        var window = new Window { Width = 1100, Height = 700, Content = vista };

        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }
```

(Verificar la firma real de `ICurrentSession` contra `src/StockApp.Application/Interfaces/ICurrentSession.cs` antes de escribir `SessionFake` — puede tener más miembros que los 4 de arriba; completar los que falten con valores neutros.)

- [ ] **Paso 4: Correr el test y verificar que pasa**

Correr, en este orden:

```bash
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ReporteTareasViewModelTests"
dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj
dotnet build tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ReporteTareasCriterioCierreAvisoTests"
```

Esperado: todos PASAN — incluido el guardián de UI de la Tarea D16 (criterio de cierre), que no debe romperse por el cambio de constructor.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Reportes/ReporteTareasViewModel.cs src/StockApp.Presentation/Views/Reportes/ReporteTareasView.axaml tests/StockApp.Presentation.Tests/ViewModels/Reportes/ReporteTareasViewModelTests.cs tests/StockApp.Presentation.UiTests/ReporteTareasCriterioCierreAvisoTests.cs
git commit -m "feat(export-pdf): agrega exportar a csv y pdf en Reporte de tareas"
```

---

## Cierre del plan

Con las Tareas 0-20 completas, las 9 pantallas del alcance (Valorización, Stock por categoría, Más movidos, Control POA, Auditoría, Historial por producto, Libro Caja, Gastos, Reporte de tareas) exportan a PDF con membrete institucional, encabezado repetido, apaisado automático en tablas de más de 6 columnas, wrap sin truncar, aviso de volumen por encima de 500 filas y oferta de apertura con el visor del sistema. El CSV existente no cambió de comportamiento en ninguna pantalla. Quedan fuera de alcance (YAGNI, ver spec): diálogo de opciones de exportación, orientación elegida por el usuario, plantillas por pantalla, subtotales/agrupaciones adicionales, firmas, marca de agua, previsualización propia, generación de PDF en el servidor, gráficos (sub-proyecto B) y las ~30 superficies restantes de exportación mecánica.

Verificación final sugerida antes de dar el incremento por cerrado:

```bash
dotnet test tests/StockApp.Documentos.Tests/StockApp.Documentos.Tests.csproj
dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj
dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj
```

Y verificación orgánica (convención del proyecto): levantar la app real, entrar a cada una de las 9 pantallas, exportar a PDF y abrir el archivo generado para confirmar visualmente membrete, columnas, apaisado donde corresponda y pie de página.
