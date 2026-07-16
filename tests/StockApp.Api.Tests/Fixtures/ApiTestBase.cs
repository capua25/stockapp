using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Licenciamiento;
using Xunit;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>
/// Base para tests de integración de StockApp.Api. Antes de cada test hace TRUNCATE
/// de todas las tablas con RESTART IDENTITY para aislar el estado — mismo patrón que
/// PostgresRepositoryTestBase en StockApp.Infrastructure.Tests (Fase 1).
/// </summary>
[Collection("Api")]
public abstract class ApiTestBase
{
    protected readonly ApiFactory Factory;

    protected ApiTestBase(ApiFactory factory)
    {
        Factory = factory;

        // Forzar la construcción del host ANTES del truncate. WebApplicationFactory arma el
        // host recién al primer acceso a Factory.Services (o CreateClient()/Server) — y ese
        // arranque corre BootstrapAdminSeeder, que siembra el usuario "admin-arranque" si la
        // tabla Usuarios está vacía. Si el truncate corriera primero, para el test que resulta
        // ser el PRIMERO de toda la collection "Api" en tocar el host, el seeder insertaría su
        // usuario DESPUÉS del truncate de ESE test — dejando una fila "colada" que ningún test
        // sembró explícitamente, y rompiendo la asunción de "Usuarios vacío tras el ctor" que
        // usan todos los demás tests. Tocando Services primero, el seed (si corre) siempre
        // ocurre ANTES del truncate de este test, y LimpiarTablas() lo barre de forma
        // determinística sin importar el orden en que xUnit ejecute los tests de la collection.
        _ = Factory.Services;
        LimpiarTablas();

        // Cada test arranca con la licencia ACTIVA (algunos la bloquean explícitamente).
        // El EstadoLicencia es singleton y se comparte en la collection: restaurarlo evita
        // que un test de modo-bloqueado filtre estado a los demás.
        Factory.Services.GetRequiredService<EstadoLicencia>().Activada = true;
    }

    private void LimpiarTablas()
    {
        using var ctx = Factory.CrearContexto();
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", " +
            "\"AsignacionesPresupuestales\", \"LineasPoa\", \"RubrosGasto\", \"FuentesFinanciamiento\", " +
            "\"Usuarios\" RESTART IDENTITY CASCADE;");
    }
}
