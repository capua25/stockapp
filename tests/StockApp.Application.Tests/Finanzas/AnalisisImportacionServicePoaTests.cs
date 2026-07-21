using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Tests.Finanzas.Fakes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Finanzas;

/// <summary>
/// F5b Task 4: mapeo de la planilla POA (líneas + movimientos) con sus estados. La reconciliación
/// real Gastos↔POA (Conciliado/Dudoso/CompromisoSoloPoa) vive en
/// <see cref="AnalisisImportacionServiceReconciliacionTests"/>; acá solo se cubren los casos que
/// no dependen de cruzar contra Gastos reales (sin factura, o factura sin ningún candidato).
/// </summary>
public class AnalisisImportacionServicePoaTests
{
    private const int Ejercicio = 2026;

    private static readonly string[] Meses =
    {
        "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO",
        "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE",
    };

    private static PlanillaGastosOds PlanillaGastosVacia()
    {
        var filasPorMes = Meses.ToDictionary(
            m => m, m => (IReadOnlyList<FilaGastoOds>)new List<FilaGastoOds>());
        return new PlanillaGastosOds(filasPorMes, new List<LineaVariableOds>());
    }

    private static FilaPoaOds Movimiento(
        int numeroFila = 1, string? factura = "F-1", string? orden = "O-1",
        string? proveedor = "ACME SA", string? gasto = "Compra de insumos", decimal importe = 500m) =>
        new(Hoja: "B", NumeroFila: numeroFila, Factura: factura, Orden: orden,
            Proveedor: proveedor, Gasto: gasto, Importe: importe);

    private sealed record Mocks(AnalisisImportacionService Svc);

    private static Mocks Crear(
        PlanillaPoaOds poa,
        IReadOnlyList<FuenteFinanciamiento>? fuentes = null)
    {
        var parser = new PlanillaParserFake(PlanillaGastosVacia(), poa);
        var proveedoresRepo = new ProveedorRepositoryFake(new List<Proveedor>());
        var rubrosRepo = new RubroGastoRepositoryFake(new List<RubroGasto>());
        var fuentesRepo = new FuenteFinanciamientoRepositoryFake(fuentes ?? new List<FuenteFinanciamiento>());

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);

        var auth = new Mock<IAuthSvc>();

        var svc = new AnalisisImportacionService(
            parser, proveedoresRepo, rubrosRepo, fuentesRepo, session.Object, auth.Object);

        return new Mocks(svc);
    }

    [Fact]
    public async Task AnalizarAsync_LineaPoaConLiteralExistente_MapeaOk()
    {
        var linea = new LineaPoaResumenOds(
            Hoja: "B", Asignaciones: new List<AsignacionPoaOds> { new("B", 1000m, 500m) },
            Movimientos: new List<FilaPoaOds>());
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));
        var m = Crear(poa, fuentes: new List<FuenteFinanciamiento> { new() { Id = 1, Nombre = "B", Activo = true } });

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var lineaPoa = Assert.Single(resultado.LineasPoa);
        Assert.Equal("B", lineaPoa.Hoja);
        Assert.Equal(Ejercicio, lineaPoa.Ejercicio);
        Assert.Equal(EstadoFila.Ok, lineaPoa.Estado);
        Assert.Empty(lineaPoa.Motivos);
        Assert.Equal("B", lineaPoa.Literal);
        Assert.False(lineaPoa.FuenteDesconocida);
        Assert.Equal(1000m, lineaPoa.Presupuesto);
        Assert.Equal(500m, lineaPoa.SaldoPlanilla);
    }

    [Fact]
    public async Task AnalizarAsync_LineaPoaConLiteralDesconocido_EsAdvertenciaYApareceEnMaestrosNuevosFuentes()
    {
        var linea = new LineaPoaResumenOds(
            Hoja: "Z", Asignaciones: new List<AsignacionPoaOds> { new("Z", 100m, 50m) },
            Movimientos: new List<FilaPoaOds>());
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(0m, 0m));
        var m = Crear(poa, fuentes: new List<FuenteFinanciamiento> { new() { Id = 1, Nombre = "B", Activo = true } });

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var lineaPoa = Assert.Single(resultado.LineasPoa);
        Assert.Equal(EstadoFila.Advertencia, lineaPoa.Estado);
        Assert.True(lineaPoa.FuenteDesconocida);
        Assert.Contains(lineaPoa.Motivos, mot => mot.Tipo == TipoMotivo.FuenteDesconocida);
        var fuenteNueva = Assert.Single(resultado.MaestrosNuevos.Fuentes);
        Assert.Equal("Z", fuenteNueva);
    }

    [Fact]
    public async Task AnalizarAsync_MovimientoPoaSinFactura_EsCompromisoSoloPoa()
    {
        var movimiento = Movimiento(factura: null, orden: null);
        var linea = new LineaPoaResumenOds(
            Hoja: "B", Asignaciones: new List<AsignacionPoaOds> { new("B", 1000m, 500m) },
            Movimientos: new List<FilaPoaOds> { movimiento });
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));
        var m = Crear(poa, fuentes: new List<FuenteFinanciamiento> { new() { Id = 1, Nombre = "B", Activo = true } });

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var lineaPoa = Assert.Single(resultado.LineasPoa);
        var mov = Assert.Single(lineaPoa.Movimientos);
        Assert.Equal(ClasificacionReconciliacion.CompromisoSoloPoa, mov.Clasificacion);
        Assert.Equal(EstadoFila.Ok, mov.Estado);
        Assert.Null(mov.IndiceGastoConciliado);
    }

    [Fact]
    public async Task AnalizarAsync_MovimientoPoaConFacturaSinGastoQueMatchee_EsCompromisoSoloPoa()
    {
        // Task 5 invalida el comportamiento provisional de Task 4: con la reconciliación real,
        // un movimiento con factura pero SIN ningún gasto que la matchee (acá la planilla de
        // Gastos está vacía) es un compromiso, no una ambigüedad — no hay candidato con el cual
        // dudar. La reconciliación completa (Conciliado/Dudoso con gastos reales) se testea en
        // AnalisisImportacionServiceReconciliacionTests.
        var movimiento = Movimiento(factura: "F-1", orden: "O-1");
        var linea = new LineaPoaResumenOds(
            Hoja: "B", Asignaciones: new List<AsignacionPoaOds> { new("B", 1000m, 500m) },
            Movimientos: new List<FilaPoaOds> { movimiento });
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));
        var m = Crear(poa, fuentes: new List<FuenteFinanciamiento> { new() { Id = 1, Nombre = "B", Activo = true } });

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var lineaPoa = Assert.Single(resultado.LineasPoa);
        var mov = Assert.Single(lineaPoa.Movimientos);
        Assert.Equal(ClasificacionReconciliacion.CompromisoSoloPoa, mov.Clasificacion);
        Assert.Equal(EstadoFila.Ok, mov.Estado);
        Assert.Empty(mov.Motivos);
        Assert.Null(mov.IndiceGastoConciliado);
        Assert.Equal("Compra de insumos", mov.Detalle);
        Assert.Equal(500m, mov.Importe);
    }

    [Fact]
    public async Task AnalizarAsync_LineaConDosAsignaciones_EmiteDosLineaPoaAnalizadaConMovimientosSoloEnLaPrimera()
    {
        // F5b Task 4: financiamiento mixto (caso real COMPOSTERAS) — una hoja con N asignaciones
        // se aplana en N LineaPoaAnalizadaDto, mismo Hoja/Ejercicio, cada uno con su propio
        // Literal/Presupuesto/SaldoPlanilla. Los movimientos de la hoja van SOLO en la asignación
        // i==0 para no duplicar el conteo del Resumen ni la reconciliación.
        var movimiento = Movimiento();
        var linea = new LineaPoaResumenOds(
            Hoja: "COMPOSTERAS Y COMPACTADORAS",
            Asignaciones: new List<AsignacionPoaOds> { new("C", 1407252m, 1407252m), new("B", 92748m, 92748m) },
            Movimientos: new List<FilaPoaOds> { movimiento });
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(92748m, 1407252m));
        var m = Crear(poa, fuentes: new List<FuenteFinanciamiento>
        {
            new() { Id = 1, Nombre = "B", Activo = true },
            new() { Id = 2, Nombre = "C", Activo = true },
        });

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        Assert.Equal(2, resultado.LineasPoa.Count);
        Assert.All(resultado.LineasPoa, l => Assert.Equal("COMPOSTERAS Y COMPACTADORAS", l.Hoja));
        Assert.All(resultado.LineasPoa, l => Assert.Equal(Ejercicio, l.Ejercicio));

        var lineaC = resultado.LineasPoa[0];
        Assert.Equal("C", lineaC.Literal);
        Assert.Equal(1407252m, lineaC.Presupuesto);
        Assert.Equal(1407252m, lineaC.SaldoPlanilla);
        var movC = Assert.Single(lineaC.Movimientos);
        Assert.Equal(movimiento.Importe, movC.Importe);

        var lineaB = resultado.LineasPoa[1];
        Assert.Equal("B", lineaB.Literal);
        Assert.Equal(92748m, lineaB.Presupuesto);
        Assert.Equal(92748m, lineaB.SaldoPlanilla);
        Assert.Empty(lineaB.Movimientos);
    }
}
