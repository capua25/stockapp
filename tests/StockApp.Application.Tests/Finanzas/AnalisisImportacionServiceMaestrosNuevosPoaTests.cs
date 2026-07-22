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
/// Bugfix post-F5b: proveedores que aparecen ÚNICAMENTE en movimientos de la hoja POA (los
/// clasificados como <see cref="ClasificacionReconciliacion.CompromisoSoloPoa"/>, típicamente sin
/// Factura) nunca llegaban a <see cref="MaestrosNuevosDto.Proveedores"/> porque
/// <c>AnalisisImportacionService</c> solo escaneaba la hoja de Gastos para armar esa lista.
/// Confirmado contra las planillas reales: 14 proveedores (ej. "ALDO S. MARZUCA", "LYF
/// MANTENIMIENTOS") quedaban afuera, lo que rompe tanto la grilla de aprobación (F5d) como
/// <c>/confirmar</c> (rechaza el payload con 400 porque la referencia nominal no resuelve contra
/// ningún maestro existente ni declarado).
/// </summary>
public class AnalisisImportacionServiceMaestrosNuevosPoaTests
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
        int numeroFila, string? proveedor, string? factura = "F-1", string? orden = "O-1", decimal egreso = 1000m) =>
        new(
            Hoja: "ENERO", NumeroFila: numeroFila, Fecha: new DateOnly(Ejercicio, 1, 15),
            Factura: factura, Orden: orden, Proveedor: proveedor, Destino: "Destino X", Gasto: "Compra de insumos",
            Ingreso: null, Egreso: egreso, Saldo: 5000m, Literal: "B", Codigo: 4, Rubro: "Paseos Públicos");

    private static FilaPoaOds Movimiento(
        int numeroFila, string? proveedor, string? factura = null, string? orden = null, decimal importe = 500m) =>
        new(Hoja: "B", NumeroFila: numeroFila, Factura: factura, Orden: orden,
            Proveedor: proveedor, Gasto: "Compra de insumos", Importe: importe);

    private static LineaPoaResumenOds Linea(
        string hoja, IReadOnlyList<AsignacionPoaOds>? asignaciones, params FilaPoaOds[] movimientos) =>
        new(Hoja: hoja,
            Asignaciones: asignaciones ?? new List<AsignacionPoaOds> { new("B", 1000m, 500m) },
            Movimientos: movimientos.ToList());

    private sealed record Mocks(AnalisisImportacionService Svc);

    private static Mocks Crear(
        PlanillaGastosOds gastos,
        PlanillaPoaOds poa,
        IReadOnlyList<Proveedor>? proveedores = null)
    {
        var parser = new PlanillaParserFake(gastos, poa);
        var proveedoresRepo = new ProveedorRepositoryFake(proveedores ?? new List<Proveedor>());
        var rubrosRepo = new RubroGastoRepositoryFake(new List<RubroGasto>());
        var fuentesRepo = new FuenteFinanciamientoRepositoryFake(
            new List<FuenteFinanciamiento> { new() { Id = 1, Nombre = "B", Activo = true } });

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);

        var auth = new Mock<IAuthSvc>();

        var svc = new AnalisisImportacionService(
            parser, proveedoresRepo, rubrosRepo, fuentesRepo, session.Object, auth.Object);

        return new Mocks(svc);
    }

    private static PlanillaGastosOds PlanillaGastosVacia() => PlanillaConEnero();

    [Fact]
    public async Task AnalizarAsync_MovimientoPoaConProveedorSoloPoa_AparecEnMaestrosNuevosProveedores()
    {
        // Reproduce el bug: proveedor SOLO en un movimiento POA (sin Factura => CompromisoSoloPoa),
        // ausente de la hoja de Gastos y de la base.
        var movimiento = Movimiento(numeroFila: 1, proveedor: "ALDO S. MARZUCA");
        var linea = Linea("B", null, movimiento);
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));

        var m = Crear(PlanillaGastosVacia(), poa);

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        Assert.Contains("ALDO S. MARZUCA", resultado.MaestrosNuevos.Proveedores);
    }

    [Fact]
    public async Task AnalizarAsync_MovimientoPoaConProveedorYaExistenteEnBase_NoAparecEnMaestrosNuevos()
    {
        var movimiento = Movimiento(numeroFila: 1, proveedor: "ACME SA");
        var linea = Linea("B", null, movimiento);
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));

        var proveedoresBase = new List<Proveedor> { new() { Id = 1, Nombre = "ACME SA", Activo = true } };
        var m = Crear(PlanillaGastosVacia(), poa, proveedoresBase);

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        Assert.Empty(resultado.MaestrosNuevos.Proveedores);
    }

    [Fact]
    public async Task AnalizarAsync_ProveedorNuevoEnGastosYEnMovimientoPoa_NoSeDuplicaEnMaestrosNuevos()
    {
        var gastosPlanilla = PlanillaConEnero(FilaEgreso(numeroFila: 10, proveedor: "NUEVO PROVEEDOR"));

        var movimiento = Movimiento(numeroFila: 1, proveedor: "NUEVO PROVEEDOR");
        var linea = Linea("B", null, movimiento);
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));

        var m = Crear(gastosPlanilla, poa);

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var proveedor = Assert.Single(resultado.MaestrosNuevos.Proveedores);
        Assert.Equal("NUEVO PROVEEDOR", proveedor);
    }

    [Fact]
    public async Task AnalizarAsync_MovimientoPoaSinProveedor_NoRevientaNiAgregaVacio()
    {
        var movimiento = Movimiento(numeroFila: 1, proveedor: null);
        var linea = Linea("B", null, movimiento);
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));

        var m = Crear(PlanillaGastosVacia(), poa);

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        Assert.Empty(resultado.MaestrosNuevos.Proveedores);
    }

    [Fact]
    public async Task AnalizarAsync_HojaConFinanciamientoMixto_ProveedorPoaSeAgregaUnaSolaVez()
    {
        // Financiamiento mixto (F5b Task 4, caso COMPOSTERAS): los movimientos de la hoja viajan
        // solo en la asignación i==0, pero el proveedor no debe registrarse una vez por asignación.
        var movimiento = Movimiento(numeroFila: 1, proveedor: "NUEVO PROVEEDOR MIXTO");
        var asignaciones = new List<AsignacionPoaOds> { new("C", 1407252m, 1407252m), new("B", 92748m, 92748m) };
        var linea = Linea("COMPOSTERAS Y COMPACTADORAS", asignaciones, movimiento);
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(92748m, 1407252m));

        var proveedoresRepo = new List<Proveedor>();
        var parser = new PlanillaParserFake(PlanillaGastosVacia(), poa);
        var fuentesRepo = new FuenteFinanciamientoRepositoryFake(new List<FuenteFinanciamiento>
        {
            new() { Id = 1, Nombre = "B", Activo = true },
            new() { Id = 2, Nombre = "C", Activo = true },
        });
        var rubrosRepo = new RubroGastoRepositoryFake(new List<RubroGasto>());
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var auth = new Mock<IAuthSvc>();
        var svc = new AnalisisImportacionService(
            parser, new ProveedorRepositoryFake(proveedoresRepo), rubrosRepo, fuentesRepo, session.Object, auth.Object);

        var resultado = await svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var proveedor = Assert.Single(resultado.MaestrosNuevos.Proveedores);
        Assert.Equal("NUEVO PROVEEDOR MIXTO", proveedor);
    }

    [Fact]
    public async Task AnalizarAsync_MovimientoPoaConciliado_ProveedorYaContempladoPorGastosNoSeDuplica()
    {
        // El movimiento matchea (Factura+Orden) un gasto ya materializado: su proveedor ya fue
        // evaluado en el escaneo de Gastos. No debe generar una segunda entrada.
        var gastosPlanilla = PlanillaConEnero(
            FilaEgreso(numeroFila: 10, proveedor: "NUEVO PROVEEDOR CONCILIADO", factura: "F-1", orden: "O-1"));

        var movimiento = Movimiento(
            numeroFila: 1, proveedor: "NUEVO PROVEEDOR CONCILIADO", factura: "F-1", orden: "O-1");
        var linea = Linea("B", null, movimiento);
        var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));

        var m = Crear(gastosPlanilla, poa);

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var mov = Assert.Single(resultado.LineasPoa.Single().Movimientos);
        Assert.Equal(ClasificacionReconciliacion.Conciliado, mov.Clasificacion);
        var proveedor = Assert.Single(resultado.MaestrosNuevos.Proveedores);
        Assert.Equal("NUEVO PROVEEDOR CONCILIADO", proveedor);
    }
}
