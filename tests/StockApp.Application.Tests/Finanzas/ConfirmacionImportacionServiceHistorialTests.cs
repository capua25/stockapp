using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Tests.Finanzas.Fakes;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Finanzas;

public class ConfirmacionImportacionServiceHistorialTests
{
    private static (ConfirmacionImportacionService Svc, ImportacionRepositoryFake Repo) Crear(
        RolUsuario rol, IReadOnlyList<ImportacionHistorialDto>? historial = null)
    {
        var proveedoresRepo = new ProveedorRepositoryFake(new List<StockApp.Domain.Entities.Proveedor>());
        var rubrosRepo = new RubroGastoRepositoryFake(new List<StockApp.Domain.Entities.RubroGasto>());
        var fuentesRepo = new FuenteFinanciamientoRepositoryFake(new List<StockApp.Domain.Entities.FuenteFinanciamiento>());
        var lineasPoaRepo = new LineaPoaRepositoryStubFake(new List<StockApp.Domain.Entities.LineaPoa>());
        var importacionRepo = new ImportacionRepositoryFake(historial: historial);

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);

        var auth = new Mock<IAuthSvc>();
        auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.ImportarPlanillas))
            .Throws<UnauthorizedAccessException>();

        var svc = new ConfirmacionImportacionService(
            importacionRepo, proveedoresRepo, rubrosRepo, fuentesRepo, lineasPoaRepo, session.Object, auth.Object);

        return (svc, importacionRepo);
    }

    [Fact]
    public async Task ListarHistorialAsync_Operador_LanzaUnauthorized()
    {
        var (svc, repo) = Crear(RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ListarHistorialAsync());
        Assert.Equal(0, repo.VecesListarHistorialLlamado);
    }

    [Fact]
    public async Task ListarHistorialAsync_Admin_DelegaEnElRepositorioYDevuelveSuResultado()
    {
        var historial = new List<ImportacionHistorialDto>
        {
            new(Guid.NewGuid(), DateTime.UtcNow, 2026, "admin", false),
        };
        var (svc, repo) = Crear(RolUsuario.Admin, historial);

        var resultado = await svc.ListarHistorialAsync();

        Assert.Equal(1, repo.VecesListarHistorialLlamado);
        Assert.Same(historial, resultado);
    }
}
