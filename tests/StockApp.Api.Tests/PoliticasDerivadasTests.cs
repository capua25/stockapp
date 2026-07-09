using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

/// <summary>
/// Test de cierre del enfoque B (spec Fase 2b, D1): itera Permisos.Todos y verifica que
/// cada política registrada en la API autoriza EXACTAMENTE los roles que
/// AuthorizationService.TienePermiso dicta — ni más ni menos. Si alguien agrega un
/// permiso nuevo y se olvida de que el loop de Program.cs lo cubre automáticamente
/// (o si AuthorizationService cambia la tabla rol→permiso), este test lo detecta.
/// </summary>
public class PoliticasDerivadasTests : ApiTestBase
{
    public PoliticasDerivadasTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task CadaPoliticaRegistrada_AutorizaExactamenteLosRolesQueAuthorizationServiceDicta()
    {
        var provider = Factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var authService = new AuthorizationService();

        foreach (var permiso in Permisos.Todos)
        {
            var policy = await provider.GetPolicyAsync(permiso);
            Assert.True(policy is not null, $"No hay política registrada para el permiso '{permiso}'.");

            var requirement = policy!.Requirements.OfType<ClaimsAuthorizationRequirement>().Single();
            var rolesEnPolitica = requirement.AllowedValues!.ToHashSet();

            var rolesEsperados = Enum.GetValues<RolUsuario>()
                .Where(rol => authService.TienePermiso(rol, permiso))
                .Select(rol => rol.ToString())
                .ToHashSet();

            Assert.True(
                rolesEsperados.SetEquals(rolesEnPolitica),
                $"Política '{permiso}': esperados [{string.Join(",", rolesEsperados)}], " +
                $"registrados [{string.Join(",", rolesEnPolitica)}].");
        }
    }
}
