using StockApp.Application.Authorization;
using Xunit;

namespace StockApp.Application.Tests.Authorization;

public class PermisoDependenciasTests
{
    [Fact]
    public void Requisitos_TodasLasClavesYValores_EstanEnPermisosConfigurables()
    {
        foreach (var (clave, valor) in PermisoDependencias.Requisitos)
        {
            Assert.Contains(clave, AuthorizationService.PermisosConfigurables);
            Assert.Contains(valor, AuthorizationService.PermisosConfigurables);
        }
    }

    [Fact]
    public void Recomendados_TodasLasClavesYValores_EstanEnPermisosConfigurables()
    {
        foreach (var (clave, recomendacion) in PermisoDependencias.Recomendados)
        {
            Assert.Contains(clave, AuthorizationService.PermisosConfigurables);
            Assert.Contains(recomendacion.PermisoRecomendado, AuthorizationService.PermisosConfigurables);
        }
    }

    [Fact]
    public void Requisitos_NingunaClaveSeRequiereASiMisma()
    {
        foreach (var (clave, valor) in PermisoDependencias.Requisitos)
            Assert.NotEqual(clave, valor);
    }

    [Fact]
    public void Recomendados_NingunaClaveSeRecomiendaASiMisma()
    {
        foreach (var (clave, recomendacion) in PermisoDependencias.Recomendados)
            Assert.NotEqual(clave, recomendacion.PermisoRecomendado);
    }

    [Fact]
    public void Requisitos_NoTieneCiclosDirectosNiTransitivos()
    {
        // Ciclo: A requiere B (directo o transitivo) y en algún punto de la cadena se vuelve
        // a pasar por un permiso ya visitado — atrapa tanto A→B→A como cadenas más largas.
        foreach (var clave in PermisoDependencias.Requisitos.Keys)
        {
            var visitados = new HashSet<string>();
            var actual = clave;
            while (PermisoDependencias.Requisitos.TryGetValue(actual, out var siguiente))
            {
                Assert.True(visitados.Add(actual), $"Ciclo detectado en Requisitos empezando por '{clave}'.");
                actual = siguiente;
            }
        }
    }
}
