# F5a — Parser de planillas .ods — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construir el parser de las dos planillas .ods del municipio (Gastos y POA) detrás de `IPlanillaParser`, backend puro y testeable en aislamiento, que lee siempre el valor cacheado de cada celda (nunca evalúa fórmulas) y produce DTOs fieles a lo que hay en cada fila.

**Architecture:** `IPlanillaParser` se define en `StockApp.Application.Finanzas` (junto al resto de interfaces de servicios de Finanzas: `IGastoService`, `IFinanzasVistasService`, etc.); la implementación `PlanillaOdsParser` vive en `StockApp.Infrastructure.Finanzas` y se apoya en un lector de bajo nivel (`OdsContentXmlReader` + `OdsHoja` + `CeldaOds`, internos a Infrastructure) que abre el .ods como `ZipArchive`, parsea `content.xml` con `XDocument` y expone una grilla de celdas ya resuelta (repeticiones y colspan expandidos, valor cacheado leído). El parser NO toca la base de datos, no depende de `ICurrentSession`/`IAuthorizationService`, y no se registra en el DI de `StockApp.Api` — es una pieza de infraestructura pura que F5b (endpoint `/finanzas/importar/analizar`, spec §8-§9) consumirá más adelante.

**Tech Stack:** .NET 10, `System.IO.Compression.ZipArchive` + `System.Xml.Linq` (`XDocument`/`XNamespace`) — ambos del framework, sin `PackageReference` nuevo. xUnit (sin Moq: no hay dependencias que mockear). Sin Testcontainers/Postgres: los tests de este plan son unit tests puros (no heredan `PostgresRepositoryTestBase`).

## Global Constraints

- **Sin dependencias externas**: parseo con `System.IO.Compression.ZipArchive` (el .ods es un zip) + `System.Xml.Linq` (`XDocument`) para leer `content.xml`. Ambos son parte del framework — cero `PackageReference` nuevo.
- **El parser NUNCA evalúa fórmulas**: todos los importes de las planillas son fórmulas (`table:formula`); se lee SIEMPRE el valor cacheado — `office:value` para números, `office:date-value` para fechas, `office:string-value` para fórmulas de texto (ej. el `VLOOKUP` de RUBRO).
- **F5 es una función ONE-SHOT de migración** (se corre una vez al poner el sistema en producción): NO sobre-ingeniería — nada de idempotencia con constraints permanentes ni robustez ante concurrencia en esta capa. Eso, si hace falta, es de F5b/F5c (reconciliación e idempotencia por factura+orden+fecha+monto, spec §8).
- **F5a es SOLO el parser**: backend puro, testeable en aislamiento, sin DB y sin API. NO incluye: registro en el DI de `StockApp.Api`, endpoint `/finanzas/importar/*`, grilla de corrección, escritura en la base. Eso es F5b/F5c.
- TDD estricto: por cada pieza nueva, escribir el test, correrlo y verlo fallar, implementar lo mínimo, correrlo y verlo verde, commit.
- Commits frecuentes, conventional commits en español (`feat(finanzas): ...`, `test(finanzas): ...`), **sin `Co-Authored-By` ni atribución a IA**.
- No correr `dotnet build` intermedio — solo los `dotnet test` que cada task pide explícitamente.

## Decisión registrada: alcance exacto de F5a

Spec §8 ("Importador de planillas") describe el importador completo (análisis → grilla de corrección → confirmación → escritura transaccional → idempotencia por factura+orden+fecha+monto). Este plan implementa ÚNICAMENTE el primer bullet de esa sección: *"Parseo server-side: ... la API lo parsea (zip + content.xml, sin dependencias externas). Una sola implementación, testeable con las planillas reales como fixtures."* — el resto (análisis con estados OK/advertencia/error, reconciliación Gastos↔POA, escritura en DB, permiso `ImportarPlanillas`) queda para F5b/F5c. `IPlanillaParser` no requiere autorización ni sesión porque no lee/escribe nada del dominio: solo transforma bytes de un `Stream` en DTOs.

## Decisión registrada: métodos síncronos

`IPlanillaParser.ParsearGastos`/`ParsearPoa` son **síncronos** (`PlanillaGastosOds`, no `Task<PlanillaGastosOds>`), a diferencia del resto de los servicios de Finanzas (`GastoService`, etc., todos `async`). Es una desviación deliberada: parsear un `Stream` ya en memoria es CPU-bound puro (zip + XML), sin ningún `await` real adentro — envolverlo en `Task` solo agregaría una asignación de estado sin beneficio (no hay I/O que liberar el hilo). El futuro endpoint de F5b, que sí hace I/O (recibe el multipart), puede envolver la llamada si su handler es async; eso es decisión de F5b, no de esta capa.

## Decisión registrada: clases internas + `InternalsVisibleTo`

`CeldaOds`, `OdsHoja` y `OdsContentXmlReader` son `internal` a `StockApp.Infrastructure`: son el mecanismo de bajo nivel, no una API que el resto de la app deba consumir. Lo público es `IPlanillaParser` (interfaz, en `StockApp.Application.Finanzas`) **y también** su implementación `PlanillaOdsParser` (en `StockApp.Infrastructure.Finanzas`) — esta última debe ser pública porque F5b la registra directamente en el contenedor DI de `StockApp.Api`. Solo los tipos auxiliares del reader (`CeldaOds`, `OdsHoja`, `OdsContentXmlReader`) quedan `internal`. Para testearlos directamente desde `StockApp.Infrastructure.Tests` se agrega `InternalsVisibleTo`, siguiendo el precedente ya existente en `StockApp.Presentation.csproj` (`InternalsVisibleTo` hacia `StockApp.Presentation.Tests`, usado para los ViewModels).

## Decisión registrada: alcance de hojas de Gastos

La planilla de Gastos tiene 15 hojas: `ENERO`..`DICIEMBRE`, `ANUAL`, `GRAFICAS`, `Variables`. Este parser solo lee las 12 hojas mensuales + `Variables` (lookup literal→código→rubro). `ANUAL` y `GRAFICAS` son vistas derivadas *dentro de la propia planilla* (totales y gráficos recalculados a partir de los 12 meses) — parsearlas sería trabajo redundante para una migración one-shot que ya tiene el dato fuente en las hojas mensuales.

---

## File Structure

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Application/Finanzas/IPlanillaParser.cs` | Interfaz pública del parser (2 métodos: Gastos, POA). |
| `src/StockApp.Application/Finanzas/PlanillaOdsDtos.cs` | DTOs de filas/resúmenes parseados (Gastos, Variables, POA, saldos totales). |
| `src/StockApp.Infrastructure/Finanzas/OdsContentXmlReader.cs` | Lector de bajo nivel: `CeldaOds` (valor cacheado ya resuelto), `OdsHoja` (grilla de celdas por hoja), `OdsContentXmlReader.LeerHoja` (expande repeticiones/colspan/covered-cell, corta en la fila de relleno). |
| `src/StockApp.Infrastructure/Finanzas/PlanillaOdsParser.cs` | Implementación de `IPlanillaParser`: abre el zip, ubica `content.xml`, mapea hojas → DTOs. |
| `src/StockApp.Infrastructure/StockApp.Infrastructure.csproj` | Se agrega `InternalsVisibleTo` hacia `StockApp.Infrastructure.Tests`. |
| `tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj` | Se agrega `<None Include="Fixtures\Finanzas\*.ods">` con `CopyToOutputDirectory`. |
| `tests/StockApp.Infrastructure.Tests/Fixtures/Finanzas/PlanillaGastos2026.ods` | Fixture real (datos del municipio) — **gitignored** (`.gitignore` línea ~497), NO se commitea; MSBuild la copia desde el disco local al output vía `CopyToOutputDirectory`, ver Task 1. |
| `tests/StockApp.Infrastructure.Tests/Fixtures/Finanzas/PlanillaPoa2026.ods` | Ídem, planilla POA. |
| `tests/StockApp.Infrastructure.Tests/Fixtures/Finanzas/OdsTestHelper.cs` | Helper de test compartido: arma un `.ods` sintético en memoria (`ZipArchive`), usado por `PlanillaOdsParserGastosTests` (Task 3) y `PlanillaOdsParserPoaTests` (Task 4). |
| `tests/StockApp.Infrastructure.Tests/Finanzas/FixturesPlanillasOdsTests.cs` | Smoke test: confirma que el `CopyToOutputDirectory` de los fixtures funciona. |
| `tests/StockApp.Infrastructure.Tests/Finanzas/OdsContentXmlReaderTests.cs` | Tests del lector de bajo nivel, con XML sintético chico (gotchas 1-4). |
| `tests/StockApp.Infrastructure.Tests/Finanzas/PlanillaOdsParserGastosTests.cs` | Tests de `ParsearGastos`, con .ods sintético armado en memoria. |
| `tests/StockApp.Infrastructure.Tests/Finanzas/PlanillaOdsParserPoaTests.cs` | Tests de `ParsearPoa`, ídem. |
| `tests/StockApp.Infrastructure.Tests/Finanzas/PlanillaOdsParserAceptacionTests.cs` | Test de aceptación (criterio duro): planillas reales, 3 saldos exactos. |

---

## Task 1: Fixtures de las planillas reales

**Files:**
- Create (binario): `tests/StockApp.Infrastructure.Tests/Fixtures/Finanzas/PlanillaGastos2026.ods`
- Create (binario): `tests/StockApp.Infrastructure.Tests/Fixtures/Finanzas/PlanillaPoa2026.ods`
- Modify: `tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj`
- Test: `tests/StockApp.Infrastructure.Tests/Finanzas/FixturesPlanillasOdsTests.cs` (nuevo)

**Interfaces:** ninguna (setup puro).

> **Nota para el orquestador**: estos dos archivos son datos reales del Municipio de Carmelo (planillas de gastos y POA 2026). Ya están gitignored (`.gitignore` línea ~497) — NO se commitean al repo, se distribuyen aparte. Este task los deja copiados en disco y listos para los tests (MSBuild los copia al output vía `CopyToOutputDirectory` igual, estén o no trackeados por git). El commit de este task incluye ÚNICAMENTE el cambio del `.csproj` (el `CopyToOutputDirectory`) y el test `FixturesPlanillasOdsTests.cs` — nunca los binarios `.ods`.

### Steps

- [ ] Copiar las dos planillas reales al directorio de fixtures (crear el directorio si no existe):

```bash
mkdir -p tests/StockApp.Infrastructure.Tests/Fixtures/Finanzas
cp "/mnt/c/Users/capua/OneDrive/Desktop/Planilla Gastos 2026 v3.ods" \
   tests/StockApp.Infrastructure.Tests/Fixtures/Finanzas/PlanillaGastos2026.ods
cp "/mnt/c/Users/capua/OneDrive/Desktop/PLANILLA POA 2026 detallada por lineas.ods" \
   tests/StockApp.Infrastructure.Tests/Fixtures/Finanzas/PlanillaPoa2026.ods
```

Resultado esperado: `ls tests/StockApp.Infrastructure.Tests/Fixtures/Finanzas/` muestra `PlanillaGastos2026.ods` (~150 KB) y `PlanillaPoa2026.ods` (~23 KB).

- [ ] Escribir el smoke test que falla (todavía no hay `CopyToOutputDirectory` configurado):

```csharp
// tests/StockApp.Infrastructure.Tests/Finanzas/FixturesPlanillasOdsTests.cs
using Xunit;

namespace StockApp.Infrastructure.Tests.Finanzas;

/// <summary>
/// Confirma que las 2 planillas reales del municipio (fixtures F5a) se copian al output
/// del proyecto de test. Si este test falla, revisar el &lt;None Include="Fixtures\Finanzas\*.ods"&gt;
/// de StockApp.Infrastructure.Tests.csproj.
/// </summary>
public class FixturesPlanillasOdsTests
{
    [Theory]
    [InlineData("PlanillaGastos2026.ods")]
    [InlineData("PlanillaPoa2026.ods")]
    public void Fixture_SeCopioAlOutput_ElArchivoExiste(string archivo)
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Finanzas", archivo);

        Assert.True(File.Exists(ruta), $"Falta el fixture '{archivo}' en {ruta}.");
    }
}
```

- [ ] Correr y ver que falla (el archivo no está en el output porque el csproj no lo copia todavía):
  `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~FixturesPlanillasOdsTests"`
  Resultado esperado: `Failed! - Failed: 2, Passed: 0` (ambos `Assert.True` fallan, "Falta el fixture...").

- [ ] Agregar el `CopyToOutputDirectory` al csproj:

```xml
<!-- tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj — agregar antes del cierre </Project> -->
  <ItemGroup>
    <None Include="Fixtures\Finanzas\*.ods">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [ ] Correr y ver verde:
  `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~FixturesPlanillasOdsTests"`
  Resultado esperado: `Passed! - Failed: 0, Passed: 2`.

- [ ] Commit (alcance: SOLO el `CopyToOutputDirectory` del `.csproj` + `FixturesPlanillasOdsTests.cs`; los `.ods` NO se agregan al commit, quedan gitignored — `.gitignore` línea ~497): `test(finanzas): agregar fixtures reales de planillas .ods para el parser F5a`

---

## Task 2: Lector de bajo nivel de celdas ODS (`OdsContentXmlReader`)

Este es el corazón del parser: expandir `table:number-columns-repeated`, `table:number-columns-spanned` (colspan) + `table:covered-table-cell`, cortar en la primera fila de relleno (`table:number-rows-repeated` masivo), y leer el valor cacheado correcto según el tipo de celda — incluyendo el matiz de `office:string-value` para fórmulas de texto (descubierto inspeccionando la planilla real: la columna RUBRO de Gastos se calcula con `VLOOKUP` y cachea el resultado en `office:string-value`, NO alcanza con leer `<text:p>`, aunque en la mayoría de los casos coincidan).

**Files:**
- Create: `src/StockApp.Infrastructure/Finanzas/OdsContentXmlReader.cs`
- Modify: `src/StockApp.Infrastructure/StockApp.Infrastructure.csproj`
- Test: `tests/StockApp.Infrastructure.Tests/Finanzas/OdsContentXmlReaderTests.cs` (nuevo)

**Interfaces:**
- Produces: `internal sealed record CeldaOds(string? Texto, decimal? Numero, DateOnly? Fecha)` con `EsVacia` y `ComoTexto()`; `internal sealed class OdsHoja` con `Celda(int fila, int columna)`, `CeldasDeFila(int fila)`, `BuscarTexto(string texto)`, `CantidadFilas`; `internal static class OdsContentXmlReader` con `LeerHoja(XDocument, string) -> OdsHoja` y `ListarHojas(XDocument) -> IReadOnlyList<string>`.

### Steps

- [ ] Habilitar `InternalsVisibleTo` hacia el proyecto de test:

```xml
<!-- src/StockApp.Infrastructure/StockApp.Infrastructure.csproj — agregar antes del cierre </Project>
     (mismo patrón que StockApp.Presentation.csproj → StockApp.Presentation.Tests) -->
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
      <_Parameter1>StockApp.Infrastructure.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
```

- [ ] Escribir los tests que fallan:

```csharp
// tests/StockApp.Infrastructure.Tests/Finanzas/OdsContentXmlReaderTests.cs
using System.Xml.Linq;
using StockApp.Infrastructure.Finanzas;
using Xunit;

namespace StockApp.Infrastructure.Tests.Finanzas;

public class OdsContentXmlReaderTests
{
    private static XDocument Documento(string filasXml, string nombreHoja = "Hoja1") => XDocument.Parse($"""
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content
            xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
            xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"
            xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
          <office:body>
            <office:spreadsheet>
              <table:table table:name="{nombreHoja}">
                {filasXml}
              </table:table>
            </office:spreadsheet>
          </office:body>
        </office:document-content>
        """);

    [Fact]
    public void LeerHoja_CeldaTextoYFloat_LeeValoresDirectos()
    {
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>PROVEEDOR</text:p></table:table-cell>
              <table:table-cell office:value-type="float" office:value="1234.5"><text:p>1.234,50</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal("PROVEEDOR", hoja.Celda(0, 0).Texto);
        Assert.Equal(1234.5m, hoja.Celda(0, 1).Numero);
    }

    [Fact]
    public void LeerHoja_ColumnasRepetidas_AvanzaElIndiceDeColumna()
    {
        var doc = Documento("""
            <table:table-row>
              <table:table-cell table:number-columns-repeated="3"/>
              <table:table-cell office:value-type="string"><text:p>DESPUES_DE_3_VACIAS</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.True(hoja.Celda(0, 0).EsVacia);
        Assert.True(hoja.Celda(0, 2).EsVacia);
        Assert.Equal("DESPUES_DE_3_VACIAS", hoja.Celda(0, 3).Texto);
    }

    [Fact]
    public void LeerHoja_ColspanConCoveredCell_NoDesalineaLaSiguienteCeldaReal()
    {
        // Gotcha más grave (POA: ~1.631 celdas con colspan=2 en columnas de datos).
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>FACTURA</text:p></table:table-cell>
              <table:covered-table-cell/>
              <table:table-cell office:value-type="string"><text:p>ORDEN</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal("FACTURA", hoja.Celda(0, 0).Texto);
        Assert.True(hoja.Celda(0, 1).EsVacia);
        Assert.Equal("ORDEN", hoja.Celda(0, 2).Texto);
    }

    [Fact]
    public void LeerHoja_ColspanConCoveredCellRepetido_AvanzaElIndiceElNumeroCorrectoDeColumnas()
    {
        // covered-table-cell puede venir con table:number-columns-repeated (varias columnas
        // cubiertas comprimidas en un solo elemento XML) — colspan=4 real.
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string" table:number-columns-spanned="4"><text:p>GASTO</text:p></table:table-cell>
              <table:covered-table-cell table:number-columns-repeated="3"/>
              <table:table-cell office:value-type="string"><text:p>IMPORTE</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal("GASTO", hoja.Celda(0, 0).Texto);
        Assert.Equal("IMPORTE", hoja.Celda(0, 4).Texto);
    }

    [Fact]
    public void LeerHoja_FilaConNumberRowsRepeated_CortaLaLecturaAhi()
    {
        // Gotcha: LibreOffice declara hasta 1.048.576 filas por hoja; solo unas pocas tienen
        // datos, el resto es UNA fila con number-rows-repeated masivo.
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>FILA REAL</text:p></table:table-cell>
            </table:table-row>
            <table:table-row table:number-rows-repeated="1048575">
              <table:table-cell/>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal(1, hoja.CantidadFilas);
        Assert.Equal("FILA REAL", hoja.Celda(0, 0).Texto);
        Assert.True(hoja.Celda(1, 0).EsVacia);
    }

    [Fact]
    public void LeerHoja_CeldaFecha_LeeOfficeDateValue()
    {
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="date" office:date-value="2026-06-01"><text:p>01/06/26</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal(new DateOnly(2026, 6, 1), hoja.Celda(0, 0).Fecha);
    }

    [Fact]
    public void LeerHoja_CeldaStringConFormula_PrefiereOfficeStringValueSobreTextoPlano()
    {
        // Gotcha descubierto en la planilla real: RUBRO se calcula con VLOOKUP; el cache de
        // una fórmula de texto vive en office:string-value, no alcanza con leer <text:p>.
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string" office:string-value="Teatro de Verano" table:formula="of:=VLOOKUP(1;1;1)"><text:p>Teatro de Verano</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal("Teatro de Verano", hoja.Celda(0, 0).Texto);
    }

    [Fact]
    public void LeerHoja_CeldaStringSinFormula_LeeTextoDelParrafo()
    {
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>COLORLUX</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal("COLORLUX", hoja.Celda(0, 0).Texto);
    }

    [Fact]
    public void LeerHoja_CeldaStringTipoSinContenido_SeConsideraVacia()
    {
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string"/>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.True(hoja.Celda(0, 0).EsVacia);
    }

    [Fact]
    public void LeerHoja_CeldaVacia_EsVaciaVerdadero()
    {
        var doc = Documento("<table:table-row><table:table-cell/></table:table-row>");

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.True(hoja.Celda(0, 0).EsVacia);
    }

    [Fact]
    public void LeerHoja_HojaInexistente_LanzaInvalidOperationException()
    {
        var doc = Documento("<table:table-row/>");

        Assert.Throws<InvalidOperationException>(() => OdsContentXmlReader.LeerHoja(doc, "NoExiste"));
    }

    [Fact]
    public void ComoTexto_CeldaNumerica_DevuelveElNumeroComoTexto()
    {
        // Gotcha: FACTURA/ORDEN mezclan float y string en la planilla real (867331 vs "A45735")
        // — se tratan SIEMPRE como texto libre.
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="float" office:value="20207"><text:p>20207</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal("20207", hoja.Celda(0, 0).ComoTexto());
    }

    [Fact]
    public void ComoTexto_CeldaDeTexto_DevuelveElTextoDirecto()
    {
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>A45735</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal("A45735", hoja.Celda(0, 0).ComoTexto());
    }

    [Fact]
    public void BuscarTexto_DevuelveLaPosicionDeLaPrimeraCoincidencia()
    {
        var doc = Documento("""
            <table:table-row>
              <table:table-cell/>
              <table:table-cell office:value-type="string"><text:p>PRESUPUESTO</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal((0, 1), hoja.BuscarTexto("PRESUPUESTO"));
        Assert.Null(hoja.BuscarTexto("NO_EXISTE"));
    }

    [Fact]
    public void ListarHojas_DevuelveLosNombresDeTodasLasTablas()
    {
        var doc = XDocument.Parse("""
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content
                xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0">
              <office:body>
                <office:spreadsheet>
                  <table:table table:name="LINEA1"/>
                  <table:table table:name="SALDO TOTALES"/>
                </office:spreadsheet>
              </office:body>
            </office:document-content>
            """);

        var hojas = OdsContentXmlReader.ListarHojas(doc);

        Assert.Equal(new[] { "LINEA1", "SALDO TOTALES" }, hojas);
    }
}
```

- [ ] Correr y ver que falla (no compila: `StockApp.Infrastructure.Finanzas` no existe):
  `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~OdsContentXmlReaderTests"`
  Resultado esperado: error de compilación `The type or namespace name 'Finanzas' does not exist in the namespace 'StockApp.Infrastructure'`.

- [ ] Implementar el lector de bajo nivel:

```csharp
// src/StockApp.Infrastructure/Finanzas/OdsContentXmlReader.cs
using System.Globalization;
using System.Xml.Linq;

namespace StockApp.Infrastructure.Finanzas;

/// <summary>
/// Valor cacheado de una celda ODS ya resuelto (F5a: nunca se evalúan fórmulas, solo se lee
/// lo que LibreOffice/Excel dejó cacheado al guardar el archivo).
/// </summary>
internal sealed record CeldaOds(string? Texto, decimal? Numero, DateOnly? Fecha)
{
    public static readonly CeldaOds Vacia = new(null, null, null);

    public bool EsVacia => Texto is null && Numero is null && Fecha is null;

    /// <summary>
    /// Lee la celda SIEMPRE como texto libre — FACTURA/ORDEN mezclan float y string en la
    /// planilla real (ej. 867331 vs "A45735"), nunca se tratan como número.
    /// </summary>
    public string? ComoTexto() => Texto ?? Numero?.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Grilla de celdas de UNA hoja de un .ods, ya expandida (sin table:number-columns-repeated
/// ni colspan/covered-cell): cada celda con contenido vive en su índice (fila, columna) real
/// de la hoja, 0-based. Las filas de relleno finales (table:number-rows-repeated masivo) NO
/// están incluidas.
/// </summary>
internal sealed class OdsHoja
{
    private readonly Dictionary<(int Fila, int Columna), CeldaOds> _celdas;

    internal OdsHoja(Dictionary<(int, int), CeldaOds> celdas, int cantidadFilas)
    {
        _celdas = celdas;
        CantidadFilas = cantidadFilas;
    }

    /// <summary>Cantidad de filas explícitas leídas (corta antes de la fila de relleno).</summary>
    public int CantidadFilas { get; }

    public CeldaOds Celda(int fila, int columna) =>
        _celdas.TryGetValue((fila, columna), out var celda) ? celda : CeldaOds.Vacia;

    /// <summary>Todas las celdas con contenido de una fila, ordenadas por columna.</summary>
    public IEnumerable<(int Columna, CeldaOds Celda)> CeldasDeFila(int fila) =>
        _celdas.Where(kv => kv.Key.Fila == fila)
               .OrderBy(kv => kv.Key.Columna)
               .Select(kv => (kv.Key.Columna, kv.Value));

    /// <summary>
    /// Posición (fila, columna) de la primera celda (orden fila luego columna) cuyo texto
    /// coincide EXACTO. Null si no aparece. Asume que el texto buscado aparece una sola vez
    /// en la zona relevante de la hoja (cierto para los headers de estas planillas).
    /// </summary>
    public (int Fila, int Columna)? BuscarTexto(string texto) =>
        _celdas.Where(kv => kv.Value.Texto == texto)
               .OrderBy(kv => kv.Key.Fila).ThenBy(kv => kv.Key.Columna)
               .Select(kv => ((int, int)?)(kv.Key.Fila, kv.Key.Columna))
               .FirstOrDefault();
}

/// <summary>
/// Lector de bajo nivel de una hoja dentro del content.xml de un .ods (F5a). Expande
/// table:number-columns-repeated, table:number-columns-spanned (colspan) y
/// table:covered-table-cell, y corta la lectura en la primera fila con
/// table:number-rows-repeated (gotcha: LibreOffice declara hasta 1.048.576 filas por hoja,
/// pero solo unas pocas tienen datos; el resto es UNA fila con number-rows-repeated masivo).
/// Nunca evalúa fórmulas: siempre lee el valor cacheado (office:value / office:date-value /
/// office:string-value).
/// </summary>
internal static class OdsContentXmlReader
{
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    public static IReadOnlyList<string> ListarHojas(XDocument contentXml) =>
        contentXml.Descendants(Table + "table")
            .Select(t => (string)t.Attribute(Table + "name")!)
            .ToList();

    public static OdsHoja LeerHoja(XDocument contentXml, string nombreHoja)
    {
        var tablaXml = contentXml.Descendants(Table + "table")
            .FirstOrDefault(t => (string?)t.Attribute(Table + "name") == nombreHoja)
            ?? throw new InvalidOperationException($"La hoja '{nombreHoja}' no existe en el .ods.");

        var celdas = new Dictionary<(int, int), CeldaOds>();
        var fila = 0;

        foreach (var filaXml in tablaXml.Elements(Table + "table-row"))
        {
            var filasRepetidas = (int?)filaXml.Attribute(Table + "number-rows-repeated") ?? 1;
            if (filasRepetidas > 1)
                break; // fila de relleno vacía hasta el límite de la hoja — acá termina la data real.

            var columna = 0;
            foreach (var celdaXml in filaXml.Elements())
            {
                var esCubierta = celdaXml.Name == Table + "covered-table-cell";
                var repetidas = (int?)celdaXml.Attribute(Table + "number-columns-repeated") ?? 1;
                var expandidas = (int?)celdaXml.Attribute(Table + "number-columns-spanned") ?? 1;
                var avance = Math.Max(repetidas, expandidas);

                if (!esCubierta)
                {
                    var valor = LeerValor(celdaXml);
                    if (!valor.EsVacia)
                        celdas[(fila, columna)] = valor;
                }

                columna += avance;
            }

            fila++;
        }

        return new OdsHoja(celdas, fila);
    }

    private static CeldaOds LeerValor(XElement celdaXml)
    {
        var tipo = (string?)celdaXml.Attribute(Office + "value-type");

        return tipo switch
        {
            "float" => new CeldaOds(
                Texto: null,
                Numero: decimal.Parse(
                    (string)celdaXml.Attribute(Office + "value")!,
                    NumberStyles.Float, CultureInfo.InvariantCulture),
                Fecha: null),

            "date" => new CeldaOds(
                Texto: null,
                Numero: null,
                Fecha: DateOnly.ParseExact(
                    (string)celdaXml.Attribute(Office + "date-value")!,
                    "yyyy-MM-dd", CultureInfo.InvariantCulture)),

            "string" => new CeldaOds(
                // Fórmulas de texto (ej. VLOOKUP de RUBRO) cachean el resultado en
                // office:string-value; las celdas de texto planas (sin fórmula) no tienen ese
                // atributo y el valor vive en <text:p>.
                Texto: NuloSiVacio((string?)celdaXml.Attribute(Office + "string-value"))
                    ?? NuloSiVacio(string.Concat(celdaXml.Elements(Text + "p").Select(p => p.Value))),
                Numero: null,
                Fecha: null),

            _ => CeldaOds.Vacia,
        };
    }

    private static string? NuloSiVacio(string? texto) => string.IsNullOrEmpty(texto) ? null : texto;
}
```

- [ ] Correr y ver verde:
  `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~OdsContentXmlReaderTests"`
  Resultado esperado: `Passed! - Failed: 0, Passed: 15`.

- [ ] Commit: `feat(finanzas): lector de bajo nivel de celdas ODS (OdsContentXmlReader)`

---

## Task 3: `IPlanillaParser` + DTOs + `PlanillaOdsParser.ParsearGastos`

**Files:**
- Create: `src/StockApp.Application/Finanzas/IPlanillaParser.cs`
- Create: `src/StockApp.Application/Finanzas/PlanillaOdsDtos.cs`
- Create: `src/StockApp.Infrastructure/Finanzas/PlanillaOdsParser.cs`
- Create: `tests/StockApp.Infrastructure.Tests/Fixtures/Finanzas/OdsTestHelper.cs` (nuevo) — helper compartido para armar un `.ods` sintético en memoria; lo reutiliza también `PlanillaOdsParserPoaTests` en Task 4 (DRY, evita duplicar la misma lógica de `ZipArchive` en las dos suites de tests).
- Test: `tests/StockApp.Infrastructure.Tests/Finanzas/PlanillaOdsParserGastosTests.cs` (nuevo)

**Interfaces:**
- Consumes: `OdsContentXmlReader.LeerHoja`, `OdsHoja.Celda/CeldasDeFila/BuscarTexto`, `CeldaOds.ComoTexto()` (Task 2).
- Produces: `IPlanillaParser.ParsearGastos(Stream) -> PlanillaGastosOds` (completo); `IPlanillaParser.ParsearPoa(Stream) -> PlanillaPoaOds` (stub `NotImplementedException`, Task 4 lo completa). DTOs: `FilaGastoOds`, `LineaVariableOds`, `PlanillaGastosOds`, `FilaPoaOds`, `LineaPoaResumenOds`, `SaldosTotalesPoaOds`, `PlanillaPoaOds`. `OdsTestHelper.CrearOdsFalso(...)` (helper de test, consumido también por Task 4).

### Steps

> **Nota TDD**: los primeros dos pasos de esta task (DTOs y la interfaz `IPlanillaParser`) definen contratos sin ninguna lógica — records planos y una firma de interfaz. Definirlos antes de escribir un test NO viola TDD: no hay comportamiento que testear todavía. El test-primero aplica a la lógica real (`ParsearGastos`), y ese test se escribe (y se ve fallar) ANTES de implementarlo — ver el paso "Escribir el test que falla" más abajo.

- [ ] Crear los DTOs (se declaran todos ahora — Task 4 completa la implementación que usa los de POA):

```csharp
// src/StockApp.Application/Finanzas/PlanillaOdsDtos.cs
namespace StockApp.Application.Finanzas;

/// <summary>
/// Fila de una hoja mensual de la planilla de Gastos (.ods, F5a). Representa exactamente lo
/// que hay en la fila de la planilla, SIN interpretar contra maestros de la base — Factura/
/// Orden se guardan como texto libre porque la planilla mezcla números y strings en esas
/// columnas. Filas que solo arrastran el SALDO hacia abajo (sin ningún otro dato) no generan
/// FilaGastoOds — no son movimientos.
/// </summary>
public sealed record FilaGastoOds(
    string Hoja,
    int NumeroFila,
    DateOnly? Fecha,
    string? Factura,
    string? Orden,
    string? Proveedor,
    string? Destino,
    string? Gasto,
    decimal? Ingreso,
    decimal? Egreso,
    decimal? Saldo,
    string? Literal,
    int? Codigo,
    string? Rubro);

/// <summary>Fila de la hoja "Variables" de la planilla de Gastos (lookup literal→código→rubro).</summary>
public sealed record LineaVariableOds(string Literal, int Codigo, string Rubro);

/// <summary>
/// Resultado completo de parsear la planilla de Gastos (.ods, F5a): filas por cada hoja
/// mensual (ENERO..DICIEMBRE) más la hoja Variables. NO incluye ANUAL/GRAFICAS: son vistas
/// derivadas dentro de la propia planilla, no datos fuente.
/// </summary>
public sealed record PlanillaGastosOds(
    IReadOnlyDictionary<string, IReadOnlyList<FilaGastoOds>> FilasPorMes,
    IReadOnlyList<LineaVariableOds> Variables);

/// <summary>Fila de movimiento (factura imputada) dentro de una hoja de línea POA.</summary>
public sealed record FilaPoaOds(
    string Hoja,
    int NumeroFila,
    string? Factura,
    string? Orden,
    string? Proveedor,
    string? Gasto,
    decimal? Importe);

/// <summary>
/// Resumen de una línea POA (una hoja de la planilla): presupuesto asignado, saldo restante,
/// literal de financiamiento (B o C) y sus movimientos.
/// </summary>
public sealed record LineaPoaResumenOds(
    string Hoja,
    decimal Presupuesto,
    decimal Saldo,
    string Literal,
    IReadOnlyList<FilaPoaOds> Movimientos);

/// <summary>Saldos consolidados de la hoja "SALDO TOTALES" de la planilla POA.</summary>
public sealed record SaldosTotalesPoaOds(decimal SaldoLiteralB, decimal SaldoLiteralC);

/// <summary>Resultado completo de parsear la planilla POA (.ods, F5a).</summary>
public sealed record PlanillaPoaOds(
    IReadOnlyList<LineaPoaResumenOds> Lineas,
    SaldosTotalesPoaOds SaldosTotales);
```

- [ ] Crear la interfaz:

```csharp
// src/StockApp.Application/Finanzas/IPlanillaParser.cs
namespace StockApp.Application.Finanzas;

/// <summary>
/// Parser de las planillas .ods de migración (F5, one-shot): NO evalúa fórmulas, siempre lee
/// el valor cacheado que LibreOffice/Excel dejó guardado en el archivo. Puro: no toca la base
/// de datos ni el estado de la app, solo interpreta bytes de un Stream ya abierto.
/// </summary>
public interface IPlanillaParser
{
    /// <summary>Parsea la planilla de Gastos: 12 hojas mensuales + Variables.</summary>
    PlanillaGastosOds ParsearGastos(Stream odsStream);

    /// <summary>Parsea la planilla POA: una hoja por línea presupuestal + SALDO TOTALES.</summary>
    PlanillaPoaOds ParsearPoa(Stream odsStream);
}
```

- [ ] Crear el helper de test compartido (usado por esta suite y por `PlanillaOdsParserPoaTests` en Task 4 — evita duplicar la misma lógica de armado de `.ods` sintético en las dos suites):

```csharp
// tests/StockApp.Infrastructure.Tests/Fixtures/Finanzas/OdsTestHelper.cs
using System.IO.Compression;

namespace StockApp.Infrastructure.Tests.Fixtures.Finanzas;

/// <summary>
/// Helper de test compartido: arma un .ods sintético en memoria (zip con content.xml a medida)
/// para los tests de PlanillaOdsParser. Usado por PlanillaOdsParserGastosTests (Task 3) y
/// PlanillaOdsParserPoaTests (Task 4) — extraído acá para no duplicar la misma lógica de
/// ZipArchive en las dos suites (DRY).
/// </summary>
internal static class OdsTestHelper
{
    public static MemoryStream CrearOdsFalso(params (string Nombre, string FilasXml)[] hojas)
    {
        var tablas = string.Join("\n", hojas.Select(h => $"""
            <table:table table:name="{h.Nombre}">
              {h.FilasXml}
            </table:table>
            """));

        var contentXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content
                xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"
                xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
              <office:body><office:spreadsheet>{tablas}</office:spreadsheet></office:body>
            </office:document-content>
            """;

        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entrada = zip.CreateEntry("content.xml");
            using var writer = new StreamWriter(entrada.Open());
            writer.Write(contentXml);
        }
        stream.Position = 0;
        return stream;
    }
}
```

- [ ] Escribir el test que falla:

```csharp
// tests/StockApp.Infrastructure.Tests/Finanzas/PlanillaOdsParserGastosTests.cs
using StockApp.Infrastructure.Finanzas;
using Xunit;
using static StockApp.Infrastructure.Tests.Fixtures.Finanzas.OdsTestHelper;

namespace StockApp.Infrastructure.Tests.Finanzas;

public class PlanillaOdsParserGastosTests
{
    private const string FilaEncabezadoGastos = """
        <table:table-row>
          <table:table-cell office:value-type="string"><text:p>FECHA</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>FACTURA</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>ORDEN</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>PROVEEDOR</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>DESTINO</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>GASTO</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>INGRESO</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>EGRESO</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>SALDO</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>LITERAL</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>Código</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>RUBRO</text:p></table:table-cell>
        </table:table-row>
        """;

    private const string FilaEncabezadoVariables = """
        <table:table-row>
          <table:table-cell office:value-type="string"><text:p>LITERAL</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>Código</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>RUBRO</text:p></table:table-cell>
        </table:table-row>
        """;

    private static (string Nombre, string FilasXml) MesVacio(string nombre) => (nombre, FilaEncabezadoGastos);

    [Fact]
    public void ParsearGastos_HojaConUnMovimiento_MapeaTodasLasColumnas()
    {
        var filaMovimiento = """
            <table:table-row>
              <table:table-cell office:value-type="date" office:date-value="2026-06-01"><text:p>01/06/26</text:p></table:table-cell>
              <table:table-cell office:value-type="float" office:value="20207"><text:p>20207</text:p></table:table-cell>
              <table:table-cell office:value-type="float" office:value="869785"><text:p>869785</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>COLORLUX</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>TEATRO DE VERANO</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>ARTÍCULOS DE LIMPIEZA</text:p></table:table-cell>
              <table:table-cell/>
              <table:table-cell office:value-type="float" office:value="29246"><text:p>29.246,00</text:p></table:table-cell>
              <table:table-cell office:value-type="float" office:value="526543"><text:p>526.543,00</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>B</text:p></table:table-cell>
              <table:table-cell office:value-type="float" office:value="14"><text:p>14</text:p></table:table-cell>
              <table:table-cell office:value-type="string" office:string-value="Teatro de Verano" table:formula="of:=VLOOKUP(1;1;1)"><text:p>Teatro de Verano</text:p></table:table-cell>
            </table:table-row>
            """;
        var filasVariables = FilaEncabezadoVariables + """
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>B</text:p></table:table-cell>
              <table:table-cell office:value-type="float" office:value="14"><text:p>14</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>Teatro de Verano</text:p></table:table-cell>
            </table:table-row>
            """;

        using var stream = CrearOdsFalso(
            MesVacio("ENERO"), MesVacio("FEBRERO"), MesVacio("MARZO"), MesVacio("ABRIL"), MesVacio("MAYO"),
            ("JUNIO", FilaEncabezadoGastos + filaMovimiento),
            MesVacio("JULIO"), MesVacio("AGOSTO"), MesVacio("SEPTIEMBRE"), MesVacio("OCTUBRE"),
            MesVacio("NOVIEMBRE"), MesVacio("DICIEMBRE"),
            ("Variables", filasVariables));

        var resultado = new PlanillaOdsParser().ParsearGastos(stream);

        var junio = Assert.Single(resultado.FilasPorMes["JUNIO"]);
        Assert.Equal(new DateOnly(2026, 6, 1), junio.Fecha);
        Assert.Equal("20207", junio.Factura);
        Assert.Equal("869785", junio.Orden);
        Assert.Equal("COLORLUX", junio.Proveedor);
        Assert.Equal("TEATRO DE VERANO", junio.Destino);
        Assert.Equal("ARTÍCULOS DE LIMPIEZA", junio.Gasto);
        Assert.Null(junio.Ingreso);
        Assert.Equal(29246m, junio.Egreso);
        Assert.Equal(526543m, junio.Saldo);
        Assert.Equal("B", junio.Literal);
        Assert.Equal(14, junio.Codigo);
        Assert.Equal("Teatro de Verano", junio.Rubro);
        Assert.Empty(resultado.FilasPorMes["ENERO"]);

        var variable = Assert.Single(resultado.Variables);
        Assert.Equal("B", variable.Literal);
        Assert.Equal(14, variable.Codigo);
        Assert.Equal("Teatro de Verano", variable.Rubro);
    }

    [Fact]
    public void ParsearGastos_FilaQueSoloArrastraSaldoSinMovimiento_SeOmite()
    {
        // Gotcha real (JUNIO de la planilla real, filas 46-200): solo tienen la fórmula de
        // SALDO copiada hacia abajo, sin FECHA/FACTURA/PROVEEDOR — no son movimientos.
        var filaSoloSaldo = """
            <table:table-row>
              <table:table-cell/><table:table-cell/><table:table-cell/><table:table-cell/>
              <table:table-cell/><table:table-cell/><table:table-cell/><table:table-cell/>
              <table:table-cell office:value-type="float" office:value="43705"><text:p>43.705,00</text:p></table:table-cell>
              <table:table-cell/><table:table-cell/><table:table-cell/>
            </table:table-row>
            """;

        using var stream = CrearOdsFalso(
            MesVacio("ENERO"), MesVacio("FEBRERO"), MesVacio("MARZO"), MesVacio("ABRIL"), MesVacio("MAYO"),
            ("JUNIO", FilaEncabezadoGastos + filaSoloSaldo),
            MesVacio("JULIO"), MesVacio("AGOSTO"), MesVacio("SEPTIEMBRE"), MesVacio("OCTUBRE"),
            MesVacio("NOVIEMBRE"), MesVacio("DICIEMBRE"),
            ("Variables", FilaEncabezadoVariables));

        var resultado = new PlanillaOdsParser().ParsearGastos(stream);

        Assert.Empty(resultado.FilasPorMes["JUNIO"]);
    }

    [Fact]
    public void ParsearGastos_FilaDeMovimientoSinFactura_ConColspanEnProveedorDestinoGasto_SeMapeaComoIngreso()
    {
        // Gotcha real (fila "SALDO ANTERIOR"/movimientos sin factura, ej. LIT. B, multas,
        // préstamos): PROVEEDOR+DESTINO+GASTO vienen fusionados (colspan=3) en una sola
        // celda de texto, FACTURA/ORDEN quedan vacíos.
        var filaSinFactura = """
            <table:table-row>
              <table:table-cell office:value-type="date" office:date-value="2026-06-01"><text:p>01/06/26</text:p></table:table-cell>
              <table:table-cell table:number-columns-repeated="2"/>
              <table:table-cell office:value-type="string" table:number-columns-spanned="3"><text:p>LIT. B </text:p></table:table-cell>
              <table:covered-table-cell table:number-columns-repeated="2"/>
              <table:table-cell office:value-type="float" office:value="300000"><text:p>300.000,00</text:p></table:table-cell>
              <table:table-cell/>
              <table:table-cell office:value-type="float" office:value="305789"><text:p>305.789,00</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>B</text:p></table:table-cell>
            </table:table-row>
            """;

        using var stream = CrearOdsFalso(
            MesVacio("ENERO"), MesVacio("FEBRERO"), MesVacio("MARZO"), MesVacio("ABRIL"), MesVacio("MAYO"),
            ("JUNIO", FilaEncabezadoGastos + filaSinFactura),
            MesVacio("JULIO"), MesVacio("AGOSTO"), MesVacio("SEPTIEMBRE"), MesVacio("OCTUBRE"),
            MesVacio("NOVIEMBRE"), MesVacio("DICIEMBRE"),
            ("Variables", FilaEncabezadoVariables));

        var resultado = new PlanillaOdsParser().ParsearGastos(stream);

        var fila = Assert.Single(resultado.FilasPorMes["JUNIO"]);
        Assert.Null(fila.Factura);
        Assert.Null(fila.Orden);
        Assert.Equal("LIT. B ", fila.Proveedor); // colspan: cae en la columna de PROVEEDOR
        Assert.Equal(300000m, fila.Ingreso);
        Assert.Equal(305789m, fila.Saldo);
    }
}
```

- [ ] Correr y ver que falla (no compila: `PlanillaOdsParser` no existe):
  `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~PlanillaOdsParserGastosTests"`
  Resultado esperado: error de compilación `The type or namespace name 'PlanillaOdsParser' could not be found`.

- [ ] Implementar `PlanillaOdsParser` (Gastos completo; POA queda como stub — Task 4 lo completa):

```csharp
// src/StockApp.Infrastructure/Finanzas/PlanillaOdsParser.cs
using System.IO.Compression;
using System.Xml.Linq;
using StockApp.Application.Finanzas;

namespace StockApp.Infrastructure.Finanzas;

public sealed class PlanillaOdsParser : IPlanillaParser
{
    private static readonly string[] MesesGastos =
    {
        "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO",
        "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE",
    };

    public PlanillaGastosOds ParsearGastos(Stream odsStream)
    {
        var contentXml = LeerContentXml(odsStream);

        var filasPorMes = MesesGastos.ToDictionary(
            mes => mes,
            mes => (IReadOnlyList<FilaGastoOds>)ParsearHojaMesGastos(contentXml, mes));

        return new PlanillaGastosOds(filasPorMes, ParsearVariables(contentXml));
    }

    public PlanillaPoaOds ParsearPoa(Stream odsStream) => throw new NotImplementedException();

    private static XDocument LeerContentXml(Stream odsStream)
    {
        using var zip = new ZipArchive(odsStream, ZipArchiveMode.Read, leaveOpen: true);
        var entrada = zip.GetEntry("content.xml")
            ?? throw new InvalidOperationException("El archivo no es un .ods válido: falta content.xml.");
        using var contentStream = entrada.Open();
        return XDocument.Load(contentStream);
    }

    private static IReadOnlyList<FilaGastoOds> ParsearHojaMesGastos(XDocument contentXml, string nombreHoja)
    {
        var hoja = OdsContentXmlReader.LeerHoja(contentXml, nombreHoja);

        var (filaEncabezado, colFecha) = hoja.BuscarTexto("FECHA")
            ?? throw new InvalidOperationException($"La hoja '{nombreHoja}' no tiene columna FECHA.");

        var colFactura = colFecha + 1;
        var colOrden = colFecha + 2;
        var colProveedor = colFecha + 3;
        var colDestino = colFecha + 4;
        var colGasto = colFecha + 5;
        var colIngreso = colFecha + 6;
        var colEgreso = colFecha + 7;
        var colSaldo = colFecha + 8;
        var colLiteral = colFecha + 9;
        var colCodigo = colFecha + 10;
        var colRubro = colFecha + 11;

        var filas = new List<FilaGastoOds>();
        for (var f = filaEncabezado + 1; f < hoja.CantidadFilas; f++)
        {
            var fecha = hoja.Celda(f, colFecha).Fecha;
            var factura = hoja.Celda(f, colFactura).ComoTexto();
            var orden = hoja.Celda(f, colOrden).ComoTexto();
            var proveedor = hoja.Celda(f, colProveedor).Texto;
            var destino = hoja.Celda(f, colDestino).Texto;
            var gasto = hoja.Celda(f, colGasto).Texto;
            var ingreso = hoja.Celda(f, colIngreso).Numero;
            var egreso = hoja.Celda(f, colEgreso).Numero;

            var esMovimiento = fecha is not null || factura is not null || orden is not null
                || proveedor is not null || destino is not null || gasto is not null
                || ingreso is not null || egreso is not null;
            if (!esMovimiento)
                continue;

            filas.Add(new FilaGastoOds(
                Hoja: nombreHoja,
                NumeroFila: f + 1, // 1-based, como lo ve un humano en LibreOffice
                Fecha: fecha,
                Factura: factura,
                Orden: orden,
                Proveedor: proveedor,
                Destino: destino,
                Gasto: gasto,
                Ingreso: ingreso,
                Egreso: egreso,
                Saldo: hoja.Celda(f, colSaldo).Numero,
                Literal: hoja.Celda(f, colLiteral).Texto,
                Codigo: hoja.Celda(f, colCodigo).Numero is { } cod ? (int)cod : null,
                Rubro: hoja.Celda(f, colRubro).Texto));
        }

        return filas;
    }

    private static IReadOnlyList<LineaVariableOds> ParsearVariables(XDocument contentXml)
    {
        var hoja = OdsContentXmlReader.LeerHoja(contentXml, "Variables");
        var (filaEncabezado, colLiteral) = hoja.BuscarTexto("LITERAL")
            ?? throw new InvalidOperationException("La hoja 'Variables' no tiene columna LITERAL.");
        var colCodigo = colLiteral + 1;
        var colRubro = colLiteral + 2;

        var lineas = new List<LineaVariableOds>();
        for (var f = filaEncabezado + 1; f < hoja.CantidadFilas; f++)
        {
            var literal = hoja.Celda(f, colLiteral).Texto;
            var codigo = hoja.Celda(f, colCodigo).Numero;
            var rubro = hoja.Celda(f, colRubro).Texto;
            if (literal is null || codigo is null || rubro is null)
                continue;

            lineas.Add(new LineaVariableOds(literal, (int)codigo.Value, rubro));
        }

        return lineas;
    }
}
```

- [ ] Correr y ver verde:
  `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~PlanillaOdsParserGastosTests"`
  Resultado esperado: `Passed! - Failed: 0, Passed: 3`.

- [ ] Commit: `feat(finanzas): IPlanillaParser + DTOs + parseo de la planilla de Gastos (.ods)`

---

## Task 4: `PlanillaOdsParser.ParsearPoa`

**Files:**
- Modify: `src/StockApp.Infrastructure/Finanzas/PlanillaOdsParser.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Finanzas/PlanillaOdsParserPoaTests.cs` (nuevo)

**Interfaces:**
- Consumes: `OdsContentXmlReader.LeerHoja/ListarHojas`, `OdsHoja.Celda/CeldasDeFila/BuscarTexto`, `CeldaOds.ComoTexto()` (Task 2); DTOs `FilaPoaOds`, `LineaPoaResumenOds`, `SaldosTotalesPoaOds`, `PlanillaPoaOds` (Task 3); `OdsTestHelper.CrearOdsFalso(...)` (helper de test compartido creado en Task 3 — NO se redefine acá, se reutiliza).
- Produces: completa `IPlanillaParser.ParsearPoa`.

### Steps

- [ ] Escribir el test que falla (reutiliza `OdsTestHelper.CrearOdsFalso` creado en Task 3 — no se vuelve a definir acá):

```csharp
// tests/StockApp.Infrastructure.Tests/Finanzas/PlanillaOdsParserPoaTests.cs
using StockApp.Infrastructure.Finanzas;
using Xunit;
using static StockApp.Infrastructure.Tests.Fixtures.Finanzas.OdsTestHelper;

namespace StockApp.Infrastructure.Tests.Finanzas;

public class PlanillaOdsParserPoaTests
{
    // Bloque PRESUPUESTO/SALDO/LITERAL (colspan=2 en cada valor, como en la planilla real).
    private const string BloquePresupuestoLiteralB = """
        <table:table-row>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>PRESUPUESTO</text:p></table:table-cell>
          <table:covered-table-cell/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>SALDO</text:p></table:table-cell>
          <table:covered-table-cell/>
        </table:table-row>
        <table:table-row>
          <table:table-cell office:value-type="float" office:value="500000" table:number-columns-spanned="2"><text:p>500.000,00</text:p></table:table-cell>
          <table:covered-table-cell/>
          <table:table-cell office:value-type="float" office:value="360000" table:number-columns-spanned="2"><text:p>360.000,00</text:p></table:table-cell>
          <table:covered-table-cell/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>LITERAL B</text:p></table:table-cell>
          <table:covered-table-cell/>
        </table:table-row>
        """;

    // Encabezado de datos: FACTURA(2) ORDEN(2) PROVEEDOR(2) GASTO(4) IMPORTE(2) — todo colspan.
    private const string EncabezadoDatosPoa = """
        <table:table-row>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>FACTURA</text:p></table:table-cell>
          <table:covered-table-cell/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>ORDEN</text:p></table:table-cell>
          <table:covered-table-cell/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>PROVEEDOR</text:p></table:table-cell>
          <table:covered-table-cell/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="4"><text:p>GASTO</text:p></table:table-cell>
          <table:covered-table-cell table:number-columns-repeated="3"/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>IMPORTE</text:p></table:table-cell>
          <table:covered-table-cell/>
        </table:table-row>
        """;

    private const string FilaMovimientoCepillo = """
        <table:table-row>
          <table:table-cell table:number-columns-spanned="2"/><table:covered-table-cell/>
          <table:table-cell table:number-columns-spanned="2"/><table:covered-table-cell/>
          <table:table-cell table:number-columns-spanned="2"/><table:covered-table-cell/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="4"><text:p>cepillo</text:p></table:table-cell>
          <table:covered-table-cell table:number-columns-repeated="3"/>
          <table:table-cell office:value-type="float" office:value="140000" table:number-columns-spanned="2"><text:p>140.000,00</text:p></table:table-cell>
          <table:covered-table-cell/>
        </table:table-row>
        """;

    // "SALDO TOTALES": etiqueta (rowspan 2) → 1 fila separadora → valor (rowspan 2), misma
    // columna — mismo layout verificado contra la planilla real.
    private static string HojaSaldoTotales(int saldoB, int saldoC) => $"""
        <table:table-row>
          <table:table-cell office:value-type="string" table:number-columns-spanned="4"><text:p>SALDO LITERAL B</text:p></table:table-cell>
          <table:covered-table-cell table:number-columns-repeated="3"/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="4"><text:p>SALDO LITERAL C</text:p></table:table-cell>
          <table:covered-table-cell table:number-columns-repeated="3"/>
        </table:table-row>
        <table:table-row><table:table-cell table:number-columns-repeated="8"/></table:table-row>
        <table:table-row><table:table-cell table:number-columns-repeated="8"/></table:table-row>
        <table:table-row>
          <table:table-cell office:value-type="float" office:value="{saldoB}" table:number-columns-spanned="4"><text:p>{saldoB}</text:p></table:table-cell>
          <table:covered-table-cell table:number-columns-repeated="3"/>
          <table:table-cell office:value-type="float" office:value="{saldoC}" table:number-columns-spanned="4"><text:p>{saldoC}</text:p></table:table-cell>
          <table:covered-table-cell table:number-columns-repeated="3"/>
        </table:table-row>
        """;

    [Fact]
    public void ParsearPoa_UnaLineaConPresupuestoYUnMovimiento_MapeaCorrectamente()
    {
        var filasLinea = BloquePresupuestoLiteralB + EncabezadoDatosPoa + FilaMovimientoCepillo;

        using var stream = CrearOdsFalso(
            ("LINEA1", filasLinea),
            ("SALDO TOTALES", HojaSaldoTotales(6643349, 4654206)));

        var resultado = new PlanillaOdsParser().ParsearPoa(stream);

        var linea = Assert.Single(resultado.Lineas);
        Assert.Equal("LINEA1", linea.Hoja);
        Assert.Equal(500000m, linea.Presupuesto);
        Assert.Equal(360000m, linea.Saldo);
        Assert.Equal("B", linea.Literal);

        var movimiento = Assert.Single(linea.Movimientos);
        Assert.Null(movimiento.Factura);
        Assert.Null(movimiento.Orden);
        Assert.Null(movimiento.Proveedor);
        Assert.Equal("cepillo", movimiento.Gasto);
        Assert.Equal(140000m, movimiento.Importe);

        Assert.Equal(6643349m, resultado.SaldosTotales.SaldoLiteralB);
        Assert.Equal(4654206m, resultado.SaldosTotales.SaldoLiteralC);
    }

    [Fact]
    public void ParsearPoa_ExcluyeLaHojaSaldoTotalesDeLaListaDeLineas()
    {
        var filasLineaSinMovimientos = BloquePresupuestoLiteralB + EncabezadoDatosPoa;

        using var stream = CrearOdsFalso(
            ("LINEA1", filasLineaSinMovimientos),
            ("SALDO TOTALES", HojaSaldoTotales(1, 1)));

        var resultado = new PlanillaOdsParser().ParsearPoa(stream);

        var linea = Assert.Single(resultado.Lineas);
        Assert.Equal("LINEA1", linea.Hoja);
        Assert.Empty(linea.Movimientos);
    }
}
```

- [ ] Correr y ver que falla:
  `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~PlanillaOdsParserPoaTests"`
  Resultado esperado: `Failed! - Failed: 2, Passed: 0` (`System.NotImplementedException` desde `ParsearPoa`).

- [ ] Completar `ParsearPoa` en `PlanillaOdsParser` (reemplazar el stub):

```csharp
// src/StockApp.Infrastructure/Finanzas/PlanillaOdsParser.cs
// Reemplazar:
//     public PlanillaPoaOds ParsearPoa(Stream odsStream) => throw new NotImplementedException();
// por:
    public PlanillaPoaOds ParsearPoa(Stream odsStream)
    {
        var contentXml = LeerContentXml(odsStream);

        var lineas = OdsContentXmlReader.ListarHojas(contentXml)
            .Where(nombre => nombre != "SALDO TOTALES")
            .Select(nombre => ParsearLineaPoa(contentXml, nombre))
            .ToList();

        return new PlanillaPoaOds(lineas, ParsearSaldosTotales(contentXml));
    }

    private static LineaPoaResumenOds ParsearLineaPoa(XDocument contentXml, string nombreHoja)
    {
        var hoja = OdsContentXmlReader.LeerHoja(contentXml, nombreHoja);

        var (filaPresupuesto, colPresupuesto) = hoja.BuscarTexto("PRESUPUESTO")
            ?? throw new InvalidOperationException($"La hoja '{nombreHoja}' no tiene celda PRESUPUESTO.");
        var (_, colSaldoResumen) = hoja.BuscarTexto("SALDO")
            ?? throw new InvalidOperationException($"La hoja '{nombreHoja}' no tiene celda SALDO.");

        var filaValores = filaPresupuesto + 1;
        var presupuesto = hoja.Celda(filaValores, colPresupuesto).Numero
            ?? throw new InvalidOperationException($"La hoja '{nombreHoja}' no tiene valor de PRESUPUESTO.");
        var saldo = hoja.Celda(filaValores, colSaldoResumen).Numero
            ?? throw new InvalidOperationException($"La hoja '{nombreHoja}' no tiene valor de SALDO.");
        var literal = BuscarLiteralEnFila(hoja, filaValores)
            ?? throw new InvalidOperationException($"La hoja '{nombreHoja}' no tiene celda LITERAL.");

        var (filaEncabezadoDatos, colFactura) = hoja.BuscarTexto("FACTURA")
            ?? throw new InvalidOperationException($"La hoja '{nombreHoja}' no tiene columna FACTURA.");
        var colOrden = colFactura + 2;
        var colProveedor = colFactura + 4;
        var colGasto = colFactura + 6;
        var colImporte = colFactura + 10;

        var movimientos = new List<FilaPoaOds>();
        for (var f = filaEncabezadoDatos + 1; f < hoja.CantidadFilas; f++)
        {
            var factura = hoja.Celda(f, colFactura).ComoTexto();
            var orden = hoja.Celda(f, colOrden).ComoTexto();
            var proveedor = hoja.Celda(f, colProveedor).Texto;
            var gasto = hoja.Celda(f, colGasto).Texto;
            var importe = hoja.Celda(f, colImporte).Numero;

            if (factura is null && orden is null && proveedor is null && gasto is null && importe is null)
                continue;

            movimientos.Add(new FilaPoaOds(nombreHoja, f + 1, factura, orden, proveedor, gasto, importe));
        }

        return new LineaPoaResumenOds(nombreHoja, presupuesto, saldo, literal, movimientos);
    }

    private static string? BuscarLiteralEnFila(OdsHoja hoja, int fila) =>
        hoja.CeldasDeFila(fila)
            .Select(c => c.Celda.Texto)
            .FirstOrDefault(t => t is not null && t.StartsWith("LITERAL ", StringComparison.Ordinal))
            ?[8..].Trim();

    private static SaldosTotalesPoaOds ParsearSaldosTotales(XDocument contentXml)
    {
        var hoja = OdsContentXmlReader.LeerHoja(contentXml, "SALDO TOTALES");

        var (filaLiteralB, colLiteralB) = hoja.BuscarTexto("SALDO LITERAL B")
            ?? throw new InvalidOperationException("La hoja 'SALDO TOTALES' no tiene celda SALDO LITERAL B.");
        var (_, colLiteralC) = hoja.BuscarTexto("SALDO LITERAL C")
            ?? throw new InvalidOperationException("La hoja 'SALDO TOTALES' no tiene celda SALDO LITERAL C.");

        // Gotcha verificado contra la planilla real: la etiqueta ocupa 2 filas fusionadas
        // (rowspan=2), después hay 1 fila separadora, y el VALOR (también rowspan=2) está 3
        // filas más abajo, en la MISMA columna que la etiqueta.
        const int desplazamientoEtiquetaAValor = 3;
        var filaValor = filaLiteralB + desplazamientoEtiquetaAValor;

        var saldoB = hoja.Celda(filaValor, colLiteralB).Numero
            ?? throw new InvalidOperationException("No se pudo leer el valor de SALDO LITERAL B.");
        var saldoC = hoja.Celda(filaValor, colLiteralC).Numero
            ?? throw new InvalidOperationException("No se pudo leer el valor de SALDO LITERAL C.");

        return new SaldosTotalesPoaOds(saldoB, saldoC);
    }
```

- [ ] Correr y ver verde:
  `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~PlanillaOdsParserPoaTests"`
  Resultado esperado: `Passed! - Failed: 0, Passed: 2`.

- [ ] Commit: `feat(finanzas): parseo de la planilla POA (.ods)`

---

## Task 5: Test de aceptación — saldos exactos contra las planillas reales

**Files:**
- Test: `tests/StockApp.Infrastructure.Tests/Finanzas/PlanillaOdsParserAceptacionTests.cs` (nuevo)

**Interfaces:**
- Consumes: `IPlanillaParser`/`PlanillaOdsParser` (Task 3, 4), fixtures `PlanillaGastos2026.ods`/`PlanillaPoa2026.ods` (Task 1).

Este es el criterio de aceptación duro de F5a (y de spec §11): parseando las planillas reales, los saldos calculados deben coincidir EXACTO con los 3 valores cacheados en las planillas.

### Steps

- [ ] Escribir el test que falla (falla porque no se corrió antes contra el archivo real — puede fallar por columnas/hojas que difieran de lo asumido en los tests sintéticos):

```csharp
// tests/StockApp.Infrastructure.Tests/Finanzas/PlanillaOdsParserAceptacionTests.cs
using StockApp.Infrastructure.Finanzas;
using Xunit;

namespace StockApp.Infrastructure.Tests.Finanzas;

/// <summary>
/// Test de aceptación F5a (spec §11, criterio duro): parseando las DOS planillas reales del
/// municipio (fixtures en Fixtures/Finanzas/), los saldos cacheados deben coincidir EXACTO
/// con los 3 valores verificados manualmente contra las planillas: caja a junio 2026 =
/// 43.705; POA Literal B = 6.643.349; POA Literal C = 4.654.206.
/// </summary>
public class PlanillaOdsParserAceptacionTests
{
    private static string RutaFixture(string archivo) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Finanzas", archivo);

    [Fact]
    public void ParsearGastos_PlanillaReal_SaldoDeJunioEs43705()
    {
        using var stream = File.OpenRead(RutaFixture("PlanillaGastos2026.ods"));
        var parser = new PlanillaOdsParser();

        var resultado = parser.ParsearGastos(stream);

        var filasJunio = resultado.FilasPorMes["JUNIO"];
        Assert.NotEmpty(filasJunio);
        // El último movimiento del mes ya trae cacheado el saldo final (las filas
        // posteriores solo arrastran el mismo valor sin cambiarlo, y se descartan por no
        // ser movimientos — ver Task 3).
        Assert.Equal(43705m, filasJunio[^1].Saldo);
    }

    [Fact]
    public void ParsearPoa_PlanillaReal_SaldosLiteralByCCoincidenConLaPlanilla()
    {
        using var stream = File.OpenRead(RutaFixture("PlanillaPoa2026.ods"));
        var parser = new PlanillaOdsParser();

        var resultado = parser.ParsearPoa(stream);

        Assert.Equal(6643349m, resultado.SaldosTotales.SaldoLiteralB);
        Assert.Equal(4654206m, resultado.SaldosTotales.SaldoLiteralC);
    }
}
```

- [ ] Correr y ver el resultado (con el diseño de este plan, verificado línea por línea contra el XML real durante la investigación previa, se espera que pase directamente; si falla, es señal de que alguna hoja real difiere de lo asumido — revisar el mensaje de `InvalidOperationException`, que indica exactamente qué encabezado/celda no se encontró):
  `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~PlanillaOdsParserAceptacionTests"`
  Resultado esperado: `Passed! - Failed: 0, Passed: 2`.

- [ ] Si algo falla, no ajustar el número esperado — ajustar el parser hasta que lea lo que realmente hay en la planilla (el oráculo son los 3 números, no el código).

- [ ] Commit: `test(finanzas): test de aceptación F5a — saldos exactos contra las planillas reales`

---

## Cierre — suite completa

- [ ] Correr toda la suite de `StockApp.Infrastructure.Tests` (no toda la solución: F5a no toca otras capas):
  `dotnet test tests/StockApp.Infrastructure.Tests`
  Resultado esperado: `Passed! - Failed: 0` con los ~25 tests nuevos de F5a (15 Task2 + 3 Task3 + 2 Task4 + 2 Task5 + 2 Task1) sumados a los existentes.

- [ ] Nota para el orquestador (NO ejecutar acá): `PlanillaGastos2026.ods`/`PlanillaPoa2026.ods` están gitignored (`.gitignore` línea ~497) y NO se commitean al repo — son datos reales del municipio, se distribuyen aparte. Quedan como archivos sin trackear en el working tree; MSBuild los copia al output de test igual.

---

## Self-review

**1. Cobertura de gotchas (todos con test dedicado):**
- `table:number-rows-repeated` masivo → `OdsContentXmlReaderTests.LeerHoja_FilaConNumberRowsRepeated_CortaLaLecturaAhi` (Task 2) + validado contra la planilla real en Task 5 (JUNIO tiene 1.048.375 filas repetidas al final).
- `table:number-columns-repeated` → `LeerHoja_ColumnasRepetidas_AvanzaElIndiceDeColumna` (Task 2).
- Colspan + `covered-table-cell` (el más grave, POA) → `LeerHoja_ColspanConCoveredCell...` + `...CoveredCellRepetido...` (Task 2), `ParsearGastos_FilaDeMovimientoSinFactura_ConColspanEn...` (Task 3, caso real de Gastos con colspan), y todo `PlanillaOdsParserPoaTests` (Task 4, colspan=2 en cada columna de datos POA).
- FACTURA/ORDEN mezclan float/string → `ComoTexto_CeldaNumerica.../ComoTexto_CeldaDeTexto...` (Task 2) + usado en Task 3/4.
- Bonus descubierto inspeccionando la planilla real (no estaba en el brief original): `office:string-value` para fórmulas de texto (RUBRO vía `VLOOKUP`) → `LeerHoja_CeldaStringConFormula_Prefiere...` (Task 2).
- Bonus descubierto en "SALDO TOTALES": desplazamiento fijo de 3 filas entre la etiqueta fusionada y el valor fusionado → `ParsearPoa_...` (Task 4) + Task 5 contra el archivo real.

**2. Criterio de aceptación (§11 del spec):** Task 5 cubre los 3 números exactos (43.705 / 6.643.349 / 4.654.206) parseando los fixtures reales copiados en Task 1.

**3. Placeholders:** cero "TBD"/"similar a"/pasos sin código — cada paso trae el C# completo. Los tipos usados en una tarea están definidos en una tarea anterior (`CeldaOds`/`OdsHoja`/`OdsContentXmlReader` en Task 2; DTOs + `IPlanillaParser` en Task 3; `PlanillaOdsParser` se crea en Task 3 y se completa en Task 4, mismo patrón que F4 usó para `FinanzasVistasService`).

**4. Consistencia de firmas:** `IPlanillaParser` se define completo en Task 3 (`ParsearGastos` + `ParsearPoa`) y se implementa incremental (Gastos en Task 3, POA en Task 4) sin cambiar la firma pública en ningún punto. Los nombres de columna/offsets (`colFactura = colFecha + 1`, etc. en Gastos; `colOrden = colFactura + 2`, etc. en POA) se usan igual en el parser y se verificaron 1:1 contra el XML real de ambas planillas durante la investigación previa a este plan (no son una suposición).

**5. Fuera de alcance, explícito:** DI en `Program.cs`, endpoint `/finanzas/importar/*`, grilla de corrección, escritura en DB, reconciliación Gastos↔POA, idempotencia — todo eso es F5b/F5c (spec §8-§9) y no aparece en ninguna task de este plan.
