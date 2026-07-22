using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

/// <summary>
/// F5c Task 9 — criterio de aceptación duro de la fase: /analizar con las dos planillas reales
/// (gitignored, mismos fixtures que F5b) → completar programáticamente los obligatorios que el
/// análisis no trae → /confirmar → consultar la base vía Factory.CrearContexto() y verificar que
/// lo persistido reproduce los saldos reales.
///
/// Los 3 oráculos y de dónde sale cada uno (los tres se verifican contra la BASE, no contra la
/// respuesta HTTP):
/// - Caja de junio 2026 = 43.705. Σ IngresoCaja.Monto activo (Fecha ≤ 30/06) − Σ PagoGasto.Monto
///   activo de Gasto activo (Fecha ≤ 30/06). Incluye 2 Ingresos con fecha ilegible en la planilla
///   (el test les sintetiza el día 1 del mes de su propia hoja — ver comentario en
///   ArmarPayloadConfirmacion).
/// - Saldo POA Literal B = 6.341.849. Suma de SaldoPlanilla (saldo cacheado por hoja) a través de
///   las 10 hojas de Literal B — coincide exacto con Presupuesto − Σ Gasto.MontoTotal persistido,
///   porque en las 10 hojas el caché cuadra contra sus propios movimientos.
/// - Saldo POA Literal C = 4.324.206 (NO 4.174.206: ver comentario junto al assert de saldoC más
///   abajo — la hoja EVENTOS cachea $150.000 de gasto que no está respaldado por ningún
///   movimiento real de la planilla, y F5c persiste movimientos, no el caché).
///
/// NINGUNO de los dos números POA es el de la hoja "SALDO TOTALES" (6.643.349 / 4.654.206) que
/// usó el criterio de aceptación de F5b — esa hoja está desincronizada de las hojas de línea en
/// la planilla real (docs/finanzas-discrepancias-planilla-poa-2026.md).
/// </summary>
public class ImportacionAceptacionConfirmacionTests : ApiTestBase
{
    private const int Ejercicio = 2026;
    private const int CodigoRubroCompromisosPoa = 999;

    public ImportacionAceptacionConfirmacionTests(ApiFactory factory) : base(factory) { }

    private static string RutaFixture(string archivo) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Finanzas", archivo);

    [Fact]
    public async Task ConfirmarAsync_PlanillasReales_PersisteLosSaldosDeLasHojasDeLinea()
    {
        await using var ctxSeed = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctxSeed, "admin.test", "Secreta123!", RolUsuario.Admin);

        var token = Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // ── 1. /analizar con las planillas reales ───────────────────────────────────────────
        var gastosBytes = await File.ReadAllBytesAsync(RutaFixture("PlanillaGastos2026.ods"));
        var poaBytes = await File.ReadAllBytesAsync(RutaFixture("PlanillaPoa2026.ods"));

        var multipart = new MultipartFormDataContent();
        var archivoGastos = new ByteArrayContent(gastosBytes);
        archivoGastos.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.oasis.opendocument.spreadsheet");
        multipart.Add(archivoGastos, "gastos", "PlanillaGastos2026.ods");
        var archivoPoa = new ByteArrayContent(poaBytes);
        archivoPoa.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.oasis.opendocument.spreadsheet");
        multipart.Add(archivoPoa, "poa", "PlanillaPoa2026.ods");
        multipart.Add(new StringContent(Ejercicio.ToString()), "ejercicio");

        var respuestaAnalisis = await client.PostAsync("/finanzas/importar/analizar", multipart);
        Assert.Equal(HttpStatusCode.OK, respuestaAnalisis.StatusCode);
        var analisis = await respuestaAnalisis.Content.ReadFromJsonAsync<ResultadoAnalisisDto>();
        Assert.NotNull(analisis);

        // ── 2. Armar el payload de /confirmar completando lo que el análisis no trae ────────
        var payload = ArmarPayloadConfirmacion(analisis!);

        // ── 3. /confirmar ────────────────────────────────────────────────────────────────────
        var respuestaConfirmacion = await client.PostAsJsonAsync("/finanzas/importar/confirmar", payload);
        var cuerpoConfirmacion = await respuestaConfirmacion.Content.ReadAsStringAsync();
        Assert.True(respuestaConfirmacion.StatusCode == HttpStatusCode.OK, cuerpoConfirmacion);

        // ── 4. Consultar la base y verificar los saldos ─────────────────────────────────────
        await using var ctx = Factory.CrearContexto();

        // Caja junio 2026: saldo inicial + Σ ingresos activos (Ene-Jun) − Σ pagos activos de
        // gastos activos (Ene-Jun). Los gastos Contado tienen un PagoGasto automático en la
        // MISMA fecha; los compromisos POA (Credito, sin pago) correctamente NO restan acá.
        var finJunio = new DateTime(Ejercicio, 6, 30, 23, 59, 59, DateTimeKind.Utc);
        var totalIngresos = await ctx.IngresosCaja
            .Where(i => i.Activo && i.Fecha <= finJunio).SumAsync(i => (decimal?)i.Monto) ?? 0m;
        var totalPagos = await ctx.PagosGasto
            .Where(p => p.Activo && p.Gasto!.Activo && p.Fecha <= finJunio)
            .SumAsync(p => (decimal?)p.Monto) ?? 0m;
        var cajaJunio = totalIngresos - totalPagos;
        Assert.Equal(43705m, cajaJunio);

        // Saldo POA por Literal = Σ AsignacionPresupuestal.Monto (presupuesto declarado en las
        // hojas de línea) − Σ Gasto.MontoTotal de gastos activos vinculados a una LineaPoa con
        // ese Literal. Ver la nota de corrección arriba sobre por qué estos números NO son los
        // de "SALDO TOTALES" — y sobre el ajuste si PoaDudosos > 0.
        // NOTA: AsignacionPresupuestal NO tiene navegación a LineaPoa (solo el FK LineaPoaId —
        // AppDbContext.cs configura esa relación con HasOne<LineaPoa>() SIN lambda de
        // navegación, ver AsignacionPresupuestal.cs), así que el filtro por Ejercicio se hace
        // contra el set de Ids de LineaPoa del ejercicio, no contra una propiedad a.LineaPoa.
        async Task<decimal> SaldoPorLiteralAsync(string literal)
        {
            var idsLineasDelEjercicio = await ctx.LineasPoa
                .Where(l => l.Ejercicio == Ejercicio)
                .Select(l => l.Id)
                .ToListAsync();

            var presupuesto = await ctx.AsignacionesPresupuestales
                .Where(a => a.FuenteFinanciamiento!.Nombre == literal
                            && idsLineasDelEjercicio.Contains(a.LineaPoaId))
                .SumAsync(a => (decimal?)a.Monto) ?? 0m;
            var gastado = await ctx.Gastos
                .Where(g => g.Activo && g.LineaPoaId != null
                            && idsLineasDelEjercicio.Contains(g.LineaPoaId!.Value)
                            && g.FuenteFinanciamiento!.Nombre == literal)
                .SumAsync(g => (decimal?)g.MontoTotal) ?? 0m;
            return presupuesto - gastado;
        }

        var ajusteDudosos = analisis.LineasPoa
            .SelectMany(l => l.Movimientos)
            .Where(m => m.Clasificacion == ClasificacionReconciliacion.Dudoso)
            .Sum(m => m.Importe ?? 0m);

        // "B"/"C" (letra sola), NO "Literal B"/"Literal C": la columna LITERAL de la planilla
        // real (tanto en la hoja POA como en los gastos vinculados a una línea) trae la letra
        // sola — confirmado contra la base (FuenteFinanciamiento.Nombre persistido = "B"/"C") y
        // contra el análisis (LineaPoaAnalizadaDto.Literal = "B"/"C", GastoAnalizadoDto.Fuente de
        // los 2 gastos con LineaPoaAsignada = "B"). "Literal B"/"Literal C" nunca matchea nada.
        var saldoB = await SaldoPorLiteralAsync("B");
        var saldoC = await SaldoPorLiteralAsync("C");

        // Si hay movimientos Dudoso, no se convirtieron en Gasto (decisión manual, F5d) — el
        // saldo persistido queda más alto que el de "suma de las hojas de línea" en esa
        // cantidad exacta. Con la planilla real (confirmado): PoaDudosos = 0, ajusteDudosos = 0.
        //
        // Literal B: 6.341.849 es la suma de SaldoPlanilla (el saldo cacheado por hoja) a través
        // de las 10 hojas de Literal B — coincide EXACTO con lo que F5c persiste (Presupuesto −
        // Σ Gasto.MontoTotal), porque en cada una de esas 10 hojas el saldo cacheado cuadra
        // perfecto contra la suma de sus propios Movimientos parseables (verificado hoja por
        // hoja). No hace falta ajuste.
        Assert.Equal(6341849m + ajusteDudosos, saldoB);

        // Literal C: el oráculo NO es 4.174.206 (la suma de SaldoPlanilla cacheado) — es
        // 4.174.206 + 150.000 = 4.324.206. Decisión del usuario (F5c Task 9): F5c persiste los
        // MOVIMIENTOS REALES de la planilla (los que tienen datos para convertirse en un Gasto),
        // no el saldo cacheado de la hoja. La hoja "EVENTOS" (Literal C) cachea un saldo que
        // implica $300.000 de gasto, pero solo tiene UN movimiento parseable en toda la planilla:
        // un CompromisoSoloPoa de $150.000 ("SUMINISTRO 068555", sin proveedor ni factura). Los
        // otros $150.000 que el caché de esa hoja da por gastados no existen en ningún lado de la
        // planilla — ni como Gasto conciliado, ni como compromiso, ni como Dudoso — así que no hay
        // ningún GastoConfirmarDto posible que los represente sin inventar un proveedor/factura
        // que la planilla no tiene. El resto de las hojas de Literal C SÍ cuadran exacto entre su
        // caché y sus movimientos (verificado hoja por hoja, igual que en B) — el ajuste de
        // +150.000 es específico de EVENTOS, no un fudge genérico. Por eso el oráculo de C se
        // aparta de "SALDO TOTALES" (4.654.206) en −480.000 (ya documentado,
        // docs/finanzas-discrepancias-planilla-poa-2026.md) Y TAMBIÉN de la propia "suma de hojas
        // de línea" cacheada (4.174.206) en +150.000 (este hallazgo, específico de EVENTOS).
        const decimal ajusteEventosSinMovimiento = 150000m;
        Assert.Equal(4174206m + ajusteEventosSinMovimiento + ajusteDudosos, saldoC);
    }

    /// <summary>
    /// Arma el ConfirmarImportacionDto a partir del resultado de /analizar. Gotcha (F5b): en una
    /// hoja con financiamiento mixto, LineaPoaAnalizadaDto se aplana en N entradas (una por
    /// asignación) que comparten el mismo Hoja — los Movimientos SOLO viajan en la primera
    /// (Movimientos.Count > 0). Agrupar por Hoja y tomar la asignación con movimientos no
    /// vacíos es obligatorio para no perderlos al armar el payload.
    /// </summary>
    /// <summary>Nombre de hoja mensual → número de mes, para sintetizar la fecha de los Ingresos
    /// con fecha ilegible (ver comentario en <c>ingresos</c> más abajo).</summary>
    private static readonly Dictionary<string, int> MesANumero = new()
    {
        ["ENERO"] = 1, ["FEBRERO"] = 2, ["MARZO"] = 3, ["ABRIL"] = 4,
        ["MAYO"] = 5, ["JUNIO"] = 6, ["JULIO"] = 7, ["AGOSTO"] = 8,
        ["SEPTIEMBRE"] = 9, ["OCTUBRE"] = 10, ["NOVIEMBRE"] = 11, ["DICIEMBRE"] = 12,
    };

    private static ConfirmarImportacionDto ArmarPayloadConfirmacion(ResultadoAnalisisDto analisis)
    {
        // Ejercicio: se usa la CONSTANTE de la clase (el mismo valor 2026 ya mandado a
        // /analizar en el paso 1), no un valor derivado de analisis.LineasPoa[0].Ejercicio —
        // más simple y sin un fallback silencioso a DateTime.UtcNow.Year si LineasPoa viniera
        // vacío (no debería, pero un fallback a "el año de hoy" sería un bug difícil de ver).
        //
        // La planilla real de Ingresos tiene 2 filas con EstadoFila.Error por "fecha ilegible"
        // (TipoMotivo.FechaIlegible) pero con Monto real: "AJUSTES ENERO" $167.661 (hoja FEBRERO)
        // y "LIT. B" $250.000 (hoja JUNIO). Un IngresoConfirmarDto exige Fecha no nula — en
        // producción el admin la completaría con la fecha real del comprobante en la grilla de
        // F5d (spec §2.4, "no se inventa dato"); acá, mismo principio que ya se aplica al rubro
        // 999 y a "(sin fuente)"/"(sin proveedor)", el test hace de humano: sintetiza el día 1 del
        // MES DE LA HOJA (ENERO..DICIEMBRE, la única info de fecha que sí trae la fila) — no
        // inventa el mes, solo el día dentro del mes que la planilla ya indica por estar esa fila
        // en esa hoja. Decisión del usuario (F5c Task 9): el criterio de aceptación de caja de
        // junio (43.705) exige que estas 2 filas SÍ se persistan — a diferencia del rubro/fecha de
        // compromisos POA, acá el mes concreto sí importa para la aserción (decide si la fila cae
        // en Ene-Jun), así que NO alcanza con excluirlas ni con una fecha fija arbitraria; el mes
        // de la propia hoja es el único dato de fecha que la planilla realmente aporta. Filas
        // Error por Monto ilegible (no es el caso de estas 2) siguen excluidas: no hay ningún dato
        // de la fila del que sintetizar un importe sin inventarlo.
        var ingresos = analisis.Ingresos
            .Where(i => i.Monto is not null)
            .Select(i => new IngresoConfirmarDto(
                i.Fecha ?? new DateOnly(Ejercicio, MesANumero[i.HojaOrigen], 1),
                i.Concepto ?? "(sin concepto)", i.Monto!.Value,
                i.Fuente ?? "(sin fuente)"))
            .ToList();

        var gastosDeLaHoja = analisis.Gastos
            .Where(g => g.Estado != EstadoFila.Error)
            .Select(g => new GastoConfirmarDto(
                g.Proveedor ?? "(sin proveedor)", g.NumeroFactura, g.NumeroOrden,
                g.Detalle ?? "(sin detalle)", g.Destino, g.Fecha!.Value, g.Monto!.Value,
                g.Fuente ?? "(sin fuente)", g.CodigoRubro ?? CodigoRubroCompromisosPoa,
                g.LineaPoaAsignada, CondicionPago.Contado, FechaVencimiento: null))
            .ToList();

        // El admin completaría esto en la grilla de F5d (spec §2.4, "no se inventa dato") — el
        // test hace de humano para poder correr sin intervención. Ver la sección de arriba.
        var fechaFallbackCompromisos = new DateOnly(Ejercicio, 12, 31);

        var gastosDeCompromisos = analisis.LineasPoa
            .GroupBy(l => l.Hoja)
            .SelectMany(grupo =>
            {
                var conMovimientos = grupo.FirstOrDefault(l => l.Movimientos.Count > 0) ?? grupo.First();
                return conMovimientos.Movimientos
                    .Where(m => m.Clasificacion == ClasificacionReconciliacion.CompromisoSoloPoa)
                    .Select(m => new GastoConfirmarDto(
                        m.Proveedor ?? "(sin proveedor)", m.Factura, m.Orden,
                        m.Detalle ?? "(compromiso POA sin detalle)", null,
                        fechaFallbackCompromisos, m.Importe ?? 0m,
                        conMovimientos.Literal ?? "(sin literal)", CodigoRubroCompromisosPoa,
                        conMovimientos.Hoja, CondicionPago.Credito,
                        FechaVencimiento: fechaFallbackCompromisos));
            })
            .ToList();

        var lineasPoa = analisis.LineasPoa
            .GroupBy(l => l.Hoja)
            .Select(grupo => new LineaPoaConfirmarDto(
                grupo.Key,
                "Migración F5c", // el análisis (F5b) deja Programa vacío a propósito — spec §2.4
                grupo.Where(l => l.Literal is not null)
                    .Select(l => new AsignacionConfirmarDto(l.Literal!, l.Presupuesto))
                    .ToList()))
            .ToList();

        var rubrosNuevos = analisis.MaestrosNuevos.Rubros
            .Select(r => new RubroNuevoConfirmarDto(r.Codigo, r.NombreSugerido ?? $"Rubro {r.Codigo}"))
            .ToList();
        rubrosNuevos.Add(new RubroNuevoConfirmarDto(
            CodigoRubroCompromisosPoa, "Compromisos POA (sin rubro en la planilla)"));

        // La planilla real de Gastos tiene ~30 filas con la columna LITERAL vacía (advertencia
        // LiteralVacio, no error — F5b las deja pasar igual) y un puñado sin Proveedor. Igual que
        // con el rubro 999 de arriba: en producción el admin completaría la fuente/proveedor real
        // en la grilla de F5d (spec §2.4, "no se inventa dato"); acá el test hace de humano y
        // declara "(sin fuente)"/"(sin proveedor)" como maestros nuevos sintéticos para poder
        // correr sin intervención — NO es un workaround para esquivar la validación, es el mismo
        // principio ya autorizado, solo que para Fuente/Proveedor en vez de Rubro. No afecta
        // ninguno de los 3 números del criterio de aceptación: la caja de junio no filtra por
        // fuente/proveedor, y el saldo POA por Literal solo suma Gastos con LineaPoaId != null —
        // ninguna de estas filas está vinculada a una línea POA (LineaPoaAsignada es null en
        // todas), así que quedan fuera de esa cuenta sea cual sea su Fuente/Proveedor.
        var proveedoresNuevos = analisis.MaestrosNuevos.Proveedores.Append("(sin proveedor)").ToList();
        var fuentesNuevas = analisis.MaestrosNuevos.Fuentes.Append("(sin fuente)").ToList();

        return new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: false,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                proveedoresNuevos, fuentesNuevas, rubrosNuevos),
            Ingresos: ingresos,
            Gastos: gastosDeLaHoja.Concat(gastosDeCompromisos).ToList(),
            LineasPoa: lineasPoa);
    }
}
