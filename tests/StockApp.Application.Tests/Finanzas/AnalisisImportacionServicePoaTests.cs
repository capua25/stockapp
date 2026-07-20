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
/// F5b Task 4: mapeo de la planilla POA (líneas + movimientos) con sus estados. La
/// reconciliación real Gastos↔POA (Conciliado/Dudoso/CompromisoSoloPoa definitivo) es Task 5;
/// acá los movimientos con Factura reciben una clasificación PROVISIONAL (ver comentario en
/// <see cref="AnalisisImportacionService"/>).
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
            Hoja: "B", Presupuesto: 1000m, Saldo: 500m, Literal: "B",
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
            Hoja: "Z", Presupuesto: 100m, Saldo: 50m, Literal: "Z",
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
            Hoja: "B", Presupuesto: 1000m, Saldo: 500m, Literal: "B",
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
    public async Task AnalizarAsync_MovimientoPoaConFactura_EsProvisionalDudosoHastaTask5()
    {
        // Decisión Task 4: sin la reconciliación real (Task 5), un movimiento CON factura no
        // puede saberse Conciliado todavía — se deja Dudoso (Advertencia + ReconciliacionDudosa)
        // como marca provisional; Task 5 lo reclasifica a Conciliado si matchea un gasto real.
        var movimiento = Movimiento(factura: "F-1", orden: "O-1");
        var linea = new LineaPoaResumenOds(
            Hoja: "B", Presupuesto: 1000m, Saldo: 500m, Literal: "B",
            Movimientos: new List<FilaPoaOds> { movimiento });
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));
        var m = Crear(poa, fuentes: new List<FuenteFinanciamiento> { new() { Id = 1, Nombre = "B", Activo = true } });

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var lineaPoa = Assert.Single(resultado.LineasPoa);
        var mov = Assert.Single(lineaPoa.Movimientos);
        Assert.Equal(ClasificacionReconciliacion.Dudoso, mov.Clasificacion);
        Assert.Equal(EstadoFila.Advertencia, mov.Estado);
        Assert.Contains(mov.Motivos, mot => mot.Tipo == TipoMotivo.ReconciliacionDudosa);
        Assert.Null(mov.IndiceGastoConciliado);
        Assert.Equal("Compra de insumos", mov.Detalle);
        Assert.Equal(500m, mov.Importe);
    }
}
