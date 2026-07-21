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
/// F5b Task 5: reconciliación real Gastos↔POA. Cruza cada movimiento POA (con Factura+Orden)
/// contra las filas EGRESO de la planilla de Gastos y clasifica Conciliado / Dudoso /
/// CompromisoSoloPoa (spec "Reglas de reconciliación Gastos↔POA (explícitas)"). Reemplaza la
/// clasificación provisional de Task 4.
/// </summary>
public class AnalisisImportacionServiceReconciliacionTests
{
    private const int Ejercicio = 2026;

    private static readonly string[] Meses =
    {
        "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO",
        "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE",
    };

    private static PlanillaGastosOds PlanillaConEnero(params FilaGastoOds[] filasEnero)
    {
        var filasPorMes = Meses.ToDictionary(
            m => m,
            m => (IReadOnlyList<FilaGastoOds>)(m == "ENERO"
                ? filasEnero.ToList()
                : new List<FilaGastoOds>()));
        return new PlanillaGastosOds(filasPorMes, new List<LineaVariableOds>());
    }

    private static FilaGastoOds FilaEgreso(
        int numeroFila, string? factura, string? orden, decimal egreso = 1000m) =>
        new(
            Hoja: "ENERO", NumeroFila: numeroFila, Fecha: new DateOnly(Ejercicio, 1, 15),
            Factura: factura, Orden: orden, Proveedor: "ACME SA", Destino: "Destino X", Gasto: "Compra de insumos",
            Ingreso: null, Egreso: egreso, Saldo: 5000m, Literal: "A", Codigo: 4, Rubro: "Paseos Públicos");

    private static FilaPoaOds Movimiento(
        int numeroFila, string? factura, string? orden, decimal importe = 500m) =>
        new(Hoja: "B", NumeroFila: numeroFila, Factura: factura, Orden: orden,
            Proveedor: "ACME SA", Gasto: "Compra de insumos", Importe: importe);

    private static LineaPoaResumenOds Linea(string hoja, params FilaPoaOds[] movimientos) =>
        new(Hoja: hoja, Asignaciones: new List<AsignacionPoaOds> { new("B", 1000m, 500m) }, Movimientos: movimientos.ToList());

    private sealed record Mocks(AnalisisImportacionService Svc);

    private static Mocks Crear(
        PlanillaGastosOds gastos,
        PlanillaPoaOds poa,
        IReadOnlyList<Proveedor>? proveedores = null,
        IReadOnlyList<RubroGasto>? rubros = null,
        IReadOnlyList<FuenteFinanciamiento>? fuentes = null)
    {
        var parser = new PlanillaParserFake(gastos, poa);
        var proveedoresRepo = new ProveedorRepositoryFake(proveedores ?? new List<Proveedor>());
        var rubrosRepo = new RubroGastoRepositoryFake(rubros ?? new List<RubroGasto>());
        var fuentesRepo = new FuenteFinanciamientoRepositoryFake(fuentes ?? new List<FuenteFinanciamiento>());

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);

        var auth = new Mock<IAuthSvc>();

        var svc = new AnalisisImportacionService(
            parser, proveedoresRepo, rubrosRepo, fuentesRepo, session.Object, auth.Object);

        return new Mocks(svc);
    }

    private static readonly IReadOnlyList<Proveedor> ProveedoresBase =
        new List<Proveedor> { new() { Id = 1, Nombre = "ACME SA", Activo = true } };

    private static readonly IReadOnlyList<RubroGasto> RubrosBase =
        new List<RubroGasto> { new() { Id = 1, Codigo = 4, Nombre = "Paseos Públicos", Activo = true } };

    private static readonly IReadOnlyList<FuenteFinanciamiento> FuentesBase = new List<FuenteFinanciamiento>
    {
        new() { Id = 1, Nombre = "A", Activo = true },
        new() { Id = 2, Nombre = "B", Activo = true },
    };

    [Fact]
    public async Task AnalizarAsync_MovimientoConFacturaYOrdenMatcheanUnGasto_EsConciliadoYAsignaLineaPoa()
    {
        var gastosPlanilla = PlanillaConEnero(FilaEgreso(numeroFila: 10, factura: "F-1", orden: "O-1"));

        var linea = Linea("B", Movimiento(numeroFila: 1, factura: "F-1", orden: "O-1"));
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));

        var m = Crear(gastosPlanilla, poa, ProveedoresBase, RubrosBase, FuentesBase);

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var gastoResultado = Assert.Single(resultado.Gastos);
        var lineaPoa = Assert.Single(resultado.LineasPoa);
        var mov = Assert.Single(lineaPoa.Movimientos);

        Assert.Equal(ClasificacionReconciliacion.Conciliado, mov.Clasificacion);
        Assert.Equal(EstadoFila.Ok, mov.Estado);
        Assert.Empty(mov.Motivos);
        Assert.Equal(0, mov.IndiceGastoConciliado);
        Assert.Equal("B", gastoResultado.LineaPoaAsignada);
    }

    [Fact]
    public async Task AnalizarAsync_FacturaMatcheaPeroOrdenDifiere_EsDudoso()
    {
        var gastosPlanilla = PlanillaConEnero(FilaEgreso(numeroFila: 10, factura: "F-1", orden: "O-1"));

        var linea = Linea("B", Movimiento(numeroFila: 1, factura: "F-1", orden: "O-2"));
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));

        var m = Crear(gastosPlanilla, poa, ProveedoresBase, RubrosBase, FuentesBase);

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var lineaPoa = Assert.Single(resultado.LineasPoa);
        var mov = Assert.Single(lineaPoa.Movimientos);

        Assert.Equal(ClasificacionReconciliacion.Dudoso, mov.Clasificacion);
        Assert.Equal(EstadoFila.Advertencia, mov.Estado);
        Assert.Contains(mov.Motivos, mot => mot.Tipo == TipoMotivo.ReconciliacionDudosa);
        Assert.Null(mov.IndiceGastoConciliado);

        var gastoResultado = Assert.Single(resultado.Gastos);
        Assert.Null(gastoResultado.LineaPoaAsignada);
    }

    [Fact]
    public async Task AnalizarAsync_MultiplesGastosConMismaFactura_EsDudoso()
    {
        var gastosPlanilla = PlanillaConEnero(
            FilaEgreso(numeroFila: 10, factura: "F-1", orden: "O-1"),
            FilaEgreso(numeroFila: 12, factura: "F-1", orden: "O-2"));

        // La orden matchea EXACTO contra uno de los dos candidatos, pero la ambigüedad de
        // factura (>1 candidato) prevalece sobre el match exacto: sigue siendo Dudoso.
        var linea = Linea("B", Movimiento(numeroFila: 1, factura: "F-1", orden: "O-1"));
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));

        var m = Crear(gastosPlanilla, poa, ProveedoresBase, RubrosBase, FuentesBase);

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var lineaPoa = Assert.Single(resultado.LineasPoa);
        var mov = Assert.Single(lineaPoa.Movimientos);

        Assert.Equal(ClasificacionReconciliacion.Dudoso, mov.Clasificacion);
        Assert.Equal(EstadoFila.Advertencia, mov.Estado);
        Assert.Null(mov.IndiceGastoConciliado);

        Assert.All(resultado.Gastos, g => Assert.Null(g.LineaPoaAsignada));
    }

    [Fact]
    public async Task AnalizarAsync_MovimientoSinFactura_EsCompromisoSoloPoa()
    {
        var gastosPlanilla = PlanillaConEnero();

        var linea = Linea("B", Movimiento(numeroFila: 1, factura: null, orden: null));
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));

        var m = Crear(gastosPlanilla, poa, fuentes: FuentesBase);

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var lineaPoa = Assert.Single(resultado.LineasPoa);
        var mov = Assert.Single(lineaPoa.Movimientos);

        Assert.Equal(ClasificacionReconciliacion.CompromisoSoloPoa, mov.Clasificacion);
        Assert.Equal(EstadoFila.Ok, mov.Estado);
        Assert.Null(mov.IndiceGastoConciliado);
    }

    [Fact]
    public async Task AnalizarAsync_FacturaQueNoExisteEnGastos_EsCompromisoSoloPoa()
    {
        var gastosPlanilla = PlanillaConEnero(FilaEgreso(numeroFila: 10, factura: "F-1", orden: "O-1"));

        var linea = Linea("B", Movimiento(numeroFila: 1, factura: "F-999", orden: "O-1"));
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));

        var m = Crear(gastosPlanilla, poa, ProveedoresBase, RubrosBase, FuentesBase);

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var lineaPoa = Assert.Single(resultado.LineasPoa);
        var mov = Assert.Single(lineaPoa.Movimientos);

        Assert.Equal(ClasificacionReconciliacion.CompromisoSoloPoa, mov.Clasificacion);
        Assert.Equal(EstadoFila.Ok, mov.Estado);
        Assert.Null(mov.IndiceGastoConciliado);
    }

    [Fact]
    public async Task AnalizarAsync_Resumen_CuentaConciliadosDudososYCompromisos()
    {
        var gastosPlanilla = PlanillaConEnero(
            FilaEgreso(numeroFila: 10, factura: "F-1", orden: "O-1"),
            FilaEgreso(numeroFila: 12, factura: "F-2", orden: "O-2"));

        var linea = Linea(
            "B",
            Movimiento(numeroFila: 1, factura: "F-1", orden: "O-1"), // Conciliado
            Movimiento(numeroFila: 2, factura: "F-2", orden: "O-9"), // Dudoso (orden difiere)
            Movimiento(numeroFila: 3, factura: null, orden: null), // CompromisoSoloPoa (sin factura)
            Movimiento(numeroFila: 4, factura: "F-999", orden: "O-1")); // CompromisoSoloPoa (factura inexistente)
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));

        var m = Crear(gastosPlanilla, poa, ProveedoresBase, RubrosBase, FuentesBase);

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        Assert.Equal(1, resultado.Resumen.PoaConciliados);
        Assert.Equal(1, resultado.Resumen.PoaDudosos);
        Assert.Equal(2, resultado.Resumen.PoaCompromisos);
    }
}
