using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Tests.Finanzas.Fakes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Finanzas;

public class ConfirmacionImportacionServiceTests
{
    private const int Ejercicio = 2026;

    private sealed record Mocks(
        ConfirmacionImportacionService Svc, ImportacionRepositoryFake Repo, Mock<IAuthSvc> Auth);

    private static Mocks Crear(
        IReadOnlyList<Proveedor>? proveedores = null,
        IReadOnlyList<RubroGasto>? rubros = null,
        IReadOnlyList<FuenteFinanciamiento>? fuentes = null,
        IReadOnlyList<LineaPoa>? lineasPoa = null,
        RolUsuario rol = RolUsuario.Admin)
    {
        var proveedoresRepo = new ProveedorRepositoryFake(proveedores ?? new List<Proveedor>());
        var rubrosRepo = new RubroGastoRepositoryFake(rubros ?? new List<RubroGasto>());
        var fuentesRepo = new FuenteFinanciamientoRepositoryFake(fuentes ?? new List<FuenteFinanciamiento>());
        var lineasPoaRepo = new LineaPoaRepositoryStubFake(lineasPoa ?? new List<LineaPoa>());
        var importacionRepo = new ImportacionRepositoryFake();

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(new StockApp.Application.Auth.UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        var auth = new Mock<IAuthSvc>();
        auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.ImportarPlanillas))
            .Throws<UnauthorizedAccessException>();

        var svc = new ConfirmacionImportacionService(
            importacionRepo, proveedoresRepo, rubrosRepo, fuentesRepo, lineasPoaRepo, session.Object, auth.Object);

        return new Mocks(svc, importacionRepo, auth);
    }

    private static ConfirmarImportacionDto PayloadValido() => new(
        Ejercicio: Ejercicio,
        Forzar: false,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            Proveedores: new List<string> { "ACME SA" },
            Fuentes: new List<string> { "Literal A" },
            Rubros: new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }),
        Ingresos: new List<IngresoConfirmarDto>
        {
            new(new DateOnly(Ejercicio, 1, 1), "Saldo inicial", 1000m, "Literal A"),
        },
        Gastos: new List<GastoConfirmarDto>
        {
            new("ACME SA", "F-1", "O-1", "Compra de insumos", null,
                new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, null, CondicionPago.Contado, null),
        },
        LineasPoa: new List<LineaPoaConfirmarDto>());

    [Fact]
    public async Task ConfirmarAsync_Operador_LanzaUnauthorized()
    {
        var m = Crear(rol: RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => m.Svc.ConfirmarAsync(PayloadValido()));
        Assert.Equal(0, m.Repo.VecesConfirmarLlamado);
    }

    [Fact]
    public async Task ConfirmarAsync_PayloadValido_DelegaEnElRepositorioYDevuelveSuResultado()
    {
        var m = Crear();
        var payload = PayloadValido();

        var resultado = await m.Svc.ConfirmarAsync(payload);

        Assert.Equal(1, m.Repo.VecesConfirmarLlamado);
        Assert.Same(payload, m.Repo.DtoRecibido);
        Assert.Equal(1, m.Repo.UsuarioIdRecibido);
        Assert.NotEqual(Guid.Empty, resultado.IdImportacion);
    }

    [Fact]
    public async Task ConfirmarAsync_ProveedorNoExisteNiDeclarado_LanzaValidacionConLaClaveDelIndice()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            MaestrosNuevos = new MaestrosNuevosConfirmarDto(new List<string>(), new List<string> { "Literal A" },
                new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }),
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.True(ex.Errores.ContainsKey("Gastos[0].Proveedor"));
        Assert.Equal(0, m.Repo.VecesConfirmarLlamado);
    }

    [Fact]
    public async Task ConfirmarAsync_CodigoRubroNoExisteNiDeclarado_LanzaValidacionConElMensajeDelSpec()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            MaestrosNuevos = new MaestrosNuevosConfirmarDto(new List<string> { "ACME SA" },
                new List<string> { "Literal A" }, new List<RubroNuevoConfirmarDto>()),
            Gastos = new List<GastoConfirmarDto>
            {
                new("ACME SA", "F-1", "O-1", "Compra de insumos", null,
                    new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 340, null, CondicionPago.Contado, null),
            },
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.Equal(
            "El rubro 340 no existe ni fue declarado nuevo",
            ex.Errores["Gastos[0].CodigoRubro"][0]);
    }

    [Fact]
    public async Task ConfirmarAsync_ProgramaVacioEnLineaPoa_LanzaValidacionConMensajeRequerido()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            LineasPoa = new List<LineaPoaConfirmarDto>
            {
                new("COMPOSTERAS", "", new List<AsignacionConfirmarDto>
                {
                    new("Literal A", 1000m),
                }),
            },
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.Equal("Requerido", ex.Errores["LineasPoa[0].Programa"][0]);
    }

    [Fact]
    public async Task ConfirmarAsync_GastoConLineaPoaDeclaradaEnElMismoPayload_NoErrorDeValidacion()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            Gastos = new List<GastoConfirmarDto>
            {
                new("ACME SA", "F-1", "O-1", "Compra de insumos", null,
                    new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, "COMPOSTERAS", CondicionPago.Contado, null),
            },
            LineasPoa = new List<LineaPoaConfirmarDto>
            {
                new("COMPOSTERAS", "Ambiente", new List<AsignacionConfirmarDto> { new("Literal A", 1000m) }),
            },
        };

        var resultado = await m.Svc.ConfirmarAsync(payload);

        Assert.Equal(1, m.Repo.VecesConfirmarLlamado);
        Assert.NotEqual(Guid.Empty, resultado.IdImportacion);
    }

    [Fact]
    public async Task ConfirmarAsync_GastoCreditoSinFechaVencimiento_LanzaValidacionConMensajeRequerido()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            Gastos = new List<GastoConfirmarDto>
            {
                new("ACME SA", "F-9", null, "Compromiso POA", null,
                    new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, null,
                    CondicionPago.Credito, FechaVencimiento: null),
            },
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.Equal("Requerido", ex.Errores["Gastos[0].FechaVencimiento"][0]);
        Assert.Equal(0, m.Repo.VecesConfirmarLlamado);
    }

    [Fact]
    public async Task ConfirmarAsync_GastoCreditoConFechaVencimiento_NoErrorDeValidacion()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            Gastos = new List<GastoConfirmarDto>
            {
                new("ACME SA", "F-9", null, "Compromiso POA", null,
                    new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, null,
                    CondicionPago.Credito, FechaVencimiento: new DateOnly(Ejercicio, 12, 31)),
            },
        };

        var resultado = await m.Svc.ConfirmarAsync(payload);

        Assert.Equal(1, m.Repo.VecesConfirmarLlamado);
        Assert.NotEqual(Guid.Empty, resultado.IdImportacion);
    }

    [Fact]
    public async Task ConfirmarAsync_GastoContadoConFechaVencimiento_LanzaValidacionConMensajeNoCorresponde()
    {
        // Regla simétrica de GastoService.cs:274-275 (ValidarAsync): un gasto de Contado NO
        // puede llevar FechaVencimiento — mismo estado que el alta manual rechaza.
        // ImportacionRepository no pasa por GastoService, así que sin este chequeo acá el
        // importador podría persistir un estado que el dominio prohíbe por la vía normal.
        var m = Crear();
        var payload = PayloadValido() with
        {
            Gastos = new List<GastoConfirmarDto>
            {
                new("ACME SA", "F-1", "O-1", "Compra de insumos", null,
                    new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, null,
                    CondicionPago.Contado, FechaVencimiento: new DateOnly(Ejercicio, 12, 31)),
            },
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.Equal(
            "No corresponde para gastos de contado",
            ex.Errores["Gastos[0].FechaVencimiento"][0]);
        Assert.Equal(0, m.Repo.VecesConfirmarLlamado);
    }

    [Fact]
    public async Task ConfirmarAsync_IngresoConFuenteNoExisteNiDeclarada_LanzaValidacionConLaClaveDelIndice()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            Ingresos = new List<IngresoConfirmarDto>
            {
                new(new DateOnly(Ejercicio, 1, 1), "Saldo inicial", 1000m, "Literal Fantasma"),
            },
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.Equal(
            "La fuente 'Literal Fantasma' no existe ni fue declarada nueva",
            ex.Errores["Ingresos[0].Fuente"][0]);
        Assert.Equal(0, m.Repo.VecesConfirmarLlamado);
    }

    [Fact]
    public async Task ConfirmarAsync_GastoConFuenteNoExisteNiDeclarada_LanzaValidacionConLaClaveDelIndice()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            Gastos = new List<GastoConfirmarDto>
            {
                new("ACME SA", "F-1", "O-1", "Compra de insumos", null,
                    new DateOnly(Ejercicio, 1, 15), 500m, "Literal Fantasma", 1, null,
                    CondicionPago.Contado, null),
            },
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.Equal(
            "La fuente 'Literal Fantasma' no existe ni fue declarada nueva",
            ex.Errores["Gastos[0].Fuente"][0]);
        Assert.Equal(0, m.Repo.VecesConfirmarLlamado);
    }

    [Fact]
    public async Task ConfirmarAsync_IngresoConConceptoVacio_LanzaValidacionConMensajeRequerido()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            Ingresos = new List<IngresoConfirmarDto>
            {
                new(new DateOnly(Ejercicio, 1, 1), "   ", 1000m, "Literal A"),
            },
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.Equal("Requerido", ex.Errores["Ingresos[0].Concepto"][0]);
        Assert.Equal(0, m.Repo.VecesConfirmarLlamado);
    }

    [Fact]
    public async Task ConfirmarAsync_GastoConDetalleVacio_LanzaValidacionConMensajeRequerido()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            Gastos = new List<GastoConfirmarDto>
            {
                new("ACME SA", "F-1", "O-1", "  ", null,
                    new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, null,
                    CondicionPago.Contado, null),
            },
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.Equal("Requerido", ex.Errores["Gastos[0].Detalle"][0]);
        Assert.Equal(0, m.Repo.VecesConfirmarLlamado);
    }

    [Fact]
    public async Task ConfirmarAsync_GastoConLineaPoaQueNoResuelve_LanzaValidacionConLaClaveDelIndice()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            Gastos = new List<GastoConfirmarDto>
            {
                new("ACME SA", "F-1", "O-1", "Compra de insumos", null,
                    new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, "LINEA FANTASMA",
                    CondicionPago.Contado, null),
            },
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.Equal(
            "La línea POA 'LINEA FANTASMA' no existe ni fue declarada en LineasPoa",
            ex.Errores["Gastos[0].LineaPoa"][0]);
        Assert.Equal(0, m.Repo.VecesConfirmarLlamado);
    }

    [Fact]
    public async Task ConfirmarAsync_LineaPoaConNombreVacio_LanzaValidacionConMensajeRequerido()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            LineasPoa = new List<LineaPoaConfirmarDto>
            {
                new("", "Ambiente", new List<AsignacionConfirmarDto> { new("Literal A", 1000m) }),
            },
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.Equal("Requerido", ex.Errores["LineasPoa[0].Nombre"][0]);
        Assert.Equal(0, m.Repo.VecesConfirmarLlamado);
    }

    [Fact]
    public async Task ConfirmarAsync_AsignacionDeLineaPoaConFuenteQueNoResuelve_LanzaValidacionConLaClaveDelIndice()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            LineasPoa = new List<LineaPoaConfirmarDto>
            {
                new("COMPOSTERAS", "Ambiente", new List<AsignacionConfirmarDto>
                {
                    new("Literal Fantasma", 1000m),
                }),
            },
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.Equal(
            "La fuente 'Literal Fantasma' no existe ni fue declarada nueva",
            ex.Errores["LineasPoa[0].Asignaciones[0].Fuente"][0]);
        Assert.Equal(0, m.Repo.VecesConfirmarLlamado);
    }

    [Fact]
    public async Task ConfirmarAsync_RubroNuevoConNombreVacio_LanzaValidacionConMensajeRequerido()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            MaestrosNuevos = new MaestrosNuevosConfirmarDto(
                new List<string> { "ACME SA" }, new List<string> { "Literal A" },
                new List<RubroNuevoConfirmarDto> { new(1, "  ") }),
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.Equal("Requerido", ex.Errores["MaestrosNuevos.Rubros[0].Nombre"][0]);
        Assert.Equal(0, m.Repo.VecesConfirmarLlamado);
    }

    [Fact]
    public async Task ConfirmarAsync_GastoConVariasViolacionesEnLaMismaFila_AcumulaTodosLosErroresBajoSusClaves()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            Gastos = new List<GastoConfirmarDto>
            {
                // Mismo Gasto[0] viola Detalle (vacío), Proveedor (no resuelve) Y Fuente (no
                // resuelve) simultáneamente: el diccionario tiene que juntar los 3 errores bajo
                // sus 3 claves distintas para esta fila, no cortar en la primera violación.
                new("Proveedor Fantasma", "F-1", "O-1", "   ", null,
                    new DateOnly(Ejercicio, 1, 15), 500m, "Literal Fantasma", 1, null,
                    CondicionPago.Contado, null),
            },
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.Equal("Requerido", ex.Errores["Gastos[0].Detalle"][0]);
        Assert.Equal(
            "El proveedor 'Proveedor Fantasma' no existe ni fue declarado nuevo",
            ex.Errores["Gastos[0].Proveedor"][0]);
        Assert.Equal(
            "La fuente 'Literal Fantasma' no existe ni fue declarada nueva",
            ex.Errores["Gastos[0].Fuente"][0]);
        Assert.Equal(0, m.Repo.VecesConfirmarLlamado);
    }

    [Fact]
    public async Task ConfirmarAsync_ListasVacias_NoErrorDeValidacionYDelegaEnElRepositorio()
    {
        // Decisión (sin regla explícita en el spec §3 para este caso): un payload sin Ingresos,
        // Gastos ni LineasPoa no tiene ninguna fila que validar, así que ninguno de los loops de
        // ValidarAsync agrega error alguno — el comportamiento sensato es aceptar el payload
        // (una re-importación que no trae absolutamente nada no es un error de FORMA; a lo sumo
        // es una corrida sin efecto, cuyo control de negocio, si hiciera falta, sería otra regla
        // explícita — no la responsabilidad de este validador de referencias nominales).
        var m = Crear();
        var payload = new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: false,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                new List<string>(), new List<string>(), new List<RubroNuevoConfirmarDto>()),
            Ingresos: new List<IngresoConfirmarDto>(),
            Gastos: new List<GastoConfirmarDto>(),
            LineasPoa: new List<LineaPoaConfirmarDto>());

        var resultado = await m.Svc.ConfirmarAsync(payload);

        Assert.Equal(1, m.Repo.VecesConfirmarLlamado);
        Assert.Same(payload, m.Repo.DtoRecibido);
        Assert.NotEqual(Guid.Empty, resultado.IdImportacion);
    }

    [Fact]
    public async Task RevertirAsync_Operador_LanzaUnauthorized()
    {
        var m = Crear(rol: RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => m.Svc.RevertirAsync(Guid.NewGuid()));
        Assert.Equal(0, m.Repo.VecesRevertirLlamado);
    }

    [Fact]
    public async Task RevertirAsync_Admin_DelegaEnElRepositorioConElIdYElUsuario()
    {
        var m = Crear();
        var idImportacion = Guid.NewGuid();

        await m.Svc.RevertirAsync(idImportacion);

        Assert.Equal(1, m.Repo.VecesRevertirLlamado);
        Assert.Equal(idImportacion, m.Repo.IdImportacionRevertidaRecibida);
        Assert.Equal(1, m.Repo.UsuarioIdRecibido);
    }
}

/// <summary>
/// F5c Task 3: stub read-only de ILineaPoaRepository — el validador solo necesita
/// ListarTodasAsync; el resto de la interfaz no lo usa ningún test de esta clase.
/// </summary>
public sealed class LineaPoaRepositoryStubFake : ILineaPoaRepository
{
    private readonly IReadOnlyList<LineaPoa> _lineas;

    public LineaPoaRepositoryStubFake(IReadOnlyList<LineaPoa> lineas) => _lineas = lineas;

    public Task<LineaPoa?> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_lineas.FirstOrDefault(l => l.Id == id));

    public Task<IReadOnlyList<LineaPoa>> ListarTodasAsync() => Task.FromResult(_lineas);

    public Task<bool> ExisteNombreEjercicioAsync(string nombre, int ejercicio, int? excluyendoId = null) =>
        throw new NotSupportedException("El validador de confirmación solo lee.");

    public Task<int> AgregarAsync(LineaPoa linea) =>
        throw new NotSupportedException("El validador de confirmación solo lee.");

    public Task ActualizarAsync(LineaPoa linea, IReadOnlyList<AsignacionPresupuestal> nuevasAsignaciones) =>
        throw new NotSupportedException("El validador de confirmación solo lee.");

    public Task ActualizarSinAsignacionesAsync(LineaPoa linea) =>
        throw new NotSupportedException("El validador de confirmación solo lee.");
}
