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
