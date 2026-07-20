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
/// F5b Task 3: mapeo de la planilla de Gastos (Ingresos + Gastos + saldo inicial) con sus
/// estados. El mapeo POA y la reconciliación (Task 4/5) están fuera de alcance acá.
/// </summary>
public class AnalisisImportacionServiceGastosTests
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
        int numeroFila = 10, DateOnly? fecha = null, string? factura = "F-1", string? orden = "O-1",
        string proveedor = "ACME SA", string? destino = "Destino X", string? gasto = "Compra de insumos",
        decimal egreso = 1000m, decimal saldo = 5000m, string? literal = "A", int? codigo = 4,
        string? rubro = "Paseos Públicos") =>
        new(
            Hoja: "ENERO", NumeroFila: numeroFila, Fecha: fecha ?? new DateOnly(Ejercicio, 1, 15),
            Factura: factura, Orden: orden, Proveedor: proveedor, Destino: destino, Gasto: gasto,
            Ingreso: null, Egreso: egreso, Saldo: saldo, Literal: literal, Codigo: codigo, Rubro: rubro);

    private static FilaGastoOds FilaIngreso(
        int numeroFila = 11, DateOnly? fecha = null, string? proveedor = "Multas 2do. Semestre",
        string? destino = null, string? gasto = null, decimal ingreso = 150000m, decimal saldo = 194524m,
        string? literal = "Multas") =>
        new(
            Hoja: "ENERO", NumeroFila: numeroFila, Fecha: fecha ?? new DateOnly(Ejercicio, 1, 8),
            Factura: null, Orden: null, Proveedor: proveedor, Destino: destino, Gasto: gasto,
            Ingreso: ingreso, Egreso: null, Saldo: saldo, Literal: literal, Codigo: null, Rubro: null);

    private sealed record Mocks(AnalisisImportacionService Svc, Mock<IAuthSvc> Auth);

    private static Mocks Crear(
        PlanillaGastosOds gastos,
        IReadOnlyList<Proveedor>? proveedores = null,
        IReadOnlyList<RubroGasto>? rubros = null,
        IReadOnlyList<FuenteFinanciamiento>? fuentes = null,
        RolUsuario rol = RolUsuario.Admin)
    {
        var parser = new PlanillaParserFake(
            gastos, new PlanillaPoaOds(new List<LineaPoaResumenOds>(), new SaldosTotalesPoaOds(0m, 0m)));
        var proveedoresRepo = new ProveedorRepositoryFake(proveedores ?? new List<Proveedor>());
        var rubrosRepo = new RubroGastoRepositoryFake(rubros ?? new List<RubroGasto>());
        var fuentesRepo = new FuenteFinanciamientoRepositoryFake(fuentes ?? new List<FuenteFinanciamiento>());

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);

        var auth = new Mock<IAuthSvc>();
        auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.ImportarPlanillas))
            .Throws<UnauthorizedAccessException>();

        var svc = new AnalisisImportacionService(
            parser, proveedoresRepo, rubrosRepo, fuentesRepo, session.Object, auth.Object);

        return new Mocks(svc, auth);
    }

    [Fact]
    public async Task AnalizarAsync_FilaEgresoConMaestrosExistentes_EsGastoOk()
    {
        var fila = FilaEgreso();
        var planilla = PlanillaConEnero(fila);
        var m = Crear(
            planilla,
            proveedores: new List<Proveedor> { new() { Id = 1, Nombre = "ACME SA", Activo = true } },
            rubros: new List<RubroGasto> { new() { Id = 1, Codigo = 4, Nombre = "Paseos Públicos", Activo = true } },
            fuentes: new List<FuenteFinanciamiento> { new() { Id = 1, Nombre = "A", Activo = true } });

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var gasto = Assert.Single(resultado.Gastos);
        Assert.Equal(EstadoFila.Ok, gasto.Estado);
        Assert.Empty(gasto.Motivos);
        Assert.Equal(fila.Egreso, gasto.Monto);
        Assert.Equal(fila.Fecha, gasto.Fecha);
        Assert.Equal("ACME SA", gasto.Proveedor);
        Assert.False(gasto.ProveedorNuevo);
        Assert.Equal("F-1", gasto.NumeroFactura);
        Assert.Equal("O-1", gasto.NumeroOrden);
        Assert.Equal("Compra de insumos", gasto.Detalle);
        Assert.Equal("Destino X", gasto.Destino);
        Assert.Equal("A", gasto.Fuente);
        Assert.False(gasto.FuenteDesconocida);
        Assert.Equal(4, gasto.CodigoRubro);
        Assert.Equal("Paseos Públicos", gasto.Rubro);
        Assert.False(gasto.RubroDesconocido);
    }

    [Fact]
    public async Task AnalizarAsync_FilaConIngreso_GeneraIngresoAnalizadoNoGasto()
    {
        var filaEgreso = FilaEgreso(numeroFila: 10);
        var filaIngreso = FilaIngreso(numeroFila: 11);
        var planilla = PlanillaConEnero(filaEgreso, filaIngreso);
        var m = Crear(
            planilla,
            proveedores: new List<Proveedor> { new() { Id = 1, Nombre = "ACME SA", Activo = true } },
            rubros: new List<RubroGasto> { new() { Id = 1, Codigo = 4, Nombre = "Paseos Públicos", Activo = true } },
            fuentes: new List<FuenteFinanciamiento>
            {
                new() { Id = 1, Nombre = "A", Activo = true },
                new() { Id = 2, Nombre = "Multas", Activo = true },
            });

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        Assert.Single(resultado.Gastos);
        // Ingresos[0] es el saldo inicial sintético (derivado de la primer fila de ENERO);
        // Ingresos[1] es la fila de ingreso real.
        Assert.Equal(2, resultado.Ingresos.Count);
        var ingreso = resultado.Ingresos[1];
        Assert.Equal(filaIngreso.Ingreso, ingreso.Monto);
        Assert.Equal(filaIngreso.Fecha, ingreso.Fecha);
        Assert.Equal("Multas 2do. Semestre", ingreso.Concepto);
        Assert.Equal("Multas", ingreso.Fuente);
    }

    [Fact]
    public async Task AnalizarAsync_LiteralVacio_EsAdvertencia()
    {
        var fila = FilaEgreso(literal: null);
        var planilla = PlanillaConEnero(fila);
        var m = Crear(
            planilla,
            proveedores: new List<Proveedor> { new() { Id = 1, Nombre = "ACME SA", Activo = true } },
            rubros: new List<RubroGasto> { new() { Id = 1, Codigo = 4, Nombre = "Paseos Públicos", Activo = true } });

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var gasto = Assert.Single(resultado.Gastos);
        Assert.Equal(EstadoFila.Advertencia, gasto.Estado);
        Assert.Contains(gasto.Motivos, mot => mot.Tipo == TipoMotivo.LiteralVacio);
    }

    [Fact]
    public async Task AnalizarAsync_CodigoRubroInexistente_EsAdvertenciaYApareceEnMaestrosNuevos()
    {
        var fila = FilaEgreso(codigo: 99, rubro: "Rubro Fantasma");
        var planilla = PlanillaConEnero(fila);
        var m = Crear(
            planilla,
            proveedores: new List<Proveedor> { new() { Id = 1, Nombre = "ACME SA", Activo = true } },
            rubros: new List<RubroGasto> { new() { Id = 1, Codigo = 4, Nombre = "Paseos Públicos", Activo = true } });

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var gasto = Assert.Single(resultado.Gastos);
        Assert.Equal(EstadoFila.Advertencia, gasto.Estado);
        Assert.True(gasto.RubroDesconocido);
        Assert.Contains(gasto.Motivos, mot => mot.Tipo == TipoMotivo.RubroDesconocido);
        var rubroNuevo = Assert.Single(resultado.MaestrosNuevos.Rubros);
        Assert.Equal(99, rubroNuevo.Codigo);
        Assert.Equal("Rubro Fantasma", rubroNuevo.NombreSugerido);
    }

    [Fact]
    public async Task AnalizarAsync_ProveedorInexistente_EsAdvertenciaYApareceUnaSolaVezDistinct()
    {
        var fila1 = FilaEgreso(numeroFila: 10, proveedor: "Proveedor Nuevo SA");
        var fila2 = FilaEgreso(numeroFila: 12, proveedor: "PROVEEDOR NUEVO SA"); // misma, distinto casing
        var planilla = PlanillaConEnero(fila1, fila2);
        var m = Crear(
            planilla,
            rubros: new List<RubroGasto> { new() { Id = 1, Codigo = 4, Nombre = "Paseos Públicos", Activo = true } });

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        Assert.All(resultado.Gastos, g =>
        {
            Assert.Equal(EstadoFila.Advertencia, g.Estado);
            Assert.True(g.ProveedorNuevo);
            Assert.Contains(g.Motivos, mot => mot.Tipo == TipoMotivo.ProveedorNuevo);
        });
        var proveedorNuevo = Assert.Single(resultado.MaestrosNuevos.Proveedores);
        Assert.Equal("Proveedor Nuevo SA", proveedorNuevo);
    }

    [Fact]
    public async Task AnalizarAsync_FechaNulaEnFilaMovimiento_EsError()
    {
        var fila = FilaEgreso(fecha: null);
        // Forzamos Fecha=null pasando por with, ya que el helper por defecto siempre pone una fecha.
        fila = fila with { Fecha = null };
        var planilla = PlanillaConEnero(fila);
        var m = Crear(
            planilla,
            proveedores: new List<Proveedor> { new() { Id = 1, Nombre = "ACME SA", Activo = true } },
            rubros: new List<RubroGasto> { new() { Id = 1, Codigo = 4, Nombre = "Paseos Públicos", Activo = true } });

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var gasto = Assert.Single(resultado.Gastos);
        Assert.Equal(EstadoFila.Error, gasto.Estado);
        Assert.Contains(gasto.Motivos, mot => mot.Tipo == TipoMotivo.FechaIlegible);
    }

    [Fact]
    public async Task AnalizarAsync_RolOperador_LanzaUnauthorizedAccessException()
    {
        var planilla = PlanillaConEnero(FilaEgreso());
        var m = Crear(planilla, rol: RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio));
    }

    [Fact]
    public async Task AnalizarAsync_SaldoInicialEnero_SeDerivaDeLaPrimerFilaYQuedaPrimeroEnIngresos()
    {
        // Reproduce el layout real verificado contra PlanillaGastos2026.ods: la fila "SALDO
        // ANTERIOR" de ENERO no sobrevive el parseo de F5a (no tiene Fecha/Proveedor/Destino/
        // Gasto/Ingreso/Egreso: solo texto en la columna SALDO y un número 2 columnas más allá),
        // así que el análisis SIEMPRE usa el fallback: saldo inicial = Saldo - Ingreso + Egreso
        // de la primera fila real de ENERO. Con estos valores (Ingreso=150000, Saldo=194524) el
        // saldo inicial da 44524 — el mismo valor que figura en la planilla real.
        var primeraFila = FilaIngreso(numeroFila: 6, ingreso: 150000m, saldo: 194524m);
        var planilla = PlanillaConEnero(primeraFila);
        var m = Crear(
            planilla,
            fuentes: new List<FuenteFinanciamiento> { new() { Id = 1, Nombre = "Multas", Activo = true } });

        var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

        var saldoInicial = resultado.Ingresos[0];
        Assert.Equal($"Saldo inicial {Ejercicio}", saldoInicial.Concepto);
        Assert.Equal(44524m, saldoInicial.Monto);
        Assert.Equal("ENERO", saldoInicial.HojaOrigen);
        Assert.Equal(EstadoFila.Ok, saldoInicial.Estado);
    }
}
