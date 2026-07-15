using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Licenciamiento;
using Testcontainers.PostgreSql;
using Xunit;

namespace StockApp.Api.Tests;

/// <summary>
/// Prueba D9 (Fase 3a): la API migra la base por su cuenta al arrancar, sin depender de
/// que algo externo (DatabaseInitializer del desktop, StockApp.Seeder, o el propio
/// ApiFactory de los demás tests) haya migrado antes. Arma su propio WebApplicationFactory
/// contra un Postgres de Testcontainers SIN migrar — la única forma de que /auth/login
/// funcione es que Program.cs migre por sí mismo.
/// </summary>
public class ApiStartupMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Arranque_SinMigracionExterna_MigraSolaYAtiendeRequests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = _container.GetConnectionString(),
                    ["Jwt:Secret"] = "clave-de-prueba-de-al-menos-32-caracteres-arranque",
                    ["Bootstrap:AdminUser"] = "admin-arranque-migracion",
                    ["Bootstrap:Password"] = "arranque-secreta-123",
                    ["Licencia:ClavePublicaBase64"] = ClavesDePrueba.ClavePublicaBase64,
                });
            });

            // Sin este override, ServicioLicencia.CargarAlArranqueAsync usa el fingerprint
            // real del OS y el almacén de archivo real (sin licencia.lic) — la API arranca
            // bloqueada (423) y el login de este test nunca llega a evaluar credenciales.
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IFingerprintMaquina, FingerprintMaquinaFake>());
                services.Replace(ServiceDescriptor.Singleton<IAlmacenLicencia>(
                    _ => new AlmacenLicenciaEnMemoria(ClavesDePrueba.EmitirLicencia())));
            });
        });

        var client = _factory.CreateClient();

        // Login contra una BD sin usuarios: si la tabla Usuarios no existe (Program.cs no
        // migró), esto tira 500 por la excepción de Npgsql ("relation Usuarios does not
        // exist"). Con la migración automática, la tabla existe y el resultado es 401
        // (credenciales inválidas) — comportamiento correcto de negocio, no un error de infra.
        var response = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest("nadie", "nada"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
