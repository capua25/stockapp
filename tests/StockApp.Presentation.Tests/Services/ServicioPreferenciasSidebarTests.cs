using System;
using System.Collections.Generic;
using System.IO;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Mismo molde que ServicioEstadoVentanaTests: round-trip contra un path temporal, y los dos
/// casos de falla que importan (archivo inexistente, archivo corrupto) tienen que devolver null
/// en vez de tirar — si el sidebar no puede leer sus preferencias, arranca con los grupos
/// cerrados, no revienta la app.
/// </summary>
public class ServicioPreferenciasSidebarTests : IDisposable
{
    private readonly string _ruta = Path.Combine(Path.GetTempPath(), $"sidebar-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_ruta)) File.Delete(_ruta);
    }

    [Fact]
    public void Guardar_YCargar_DevuelveLosMismosGrupos()
    {
        var servicio = new ServicioPreferenciasSidebar(_ruta);
        servicio.Guardar(new PreferenciasSidebar(new List<string> { "Finanzas", "Reportes" }));

        var leido = new ServicioPreferenciasSidebar(_ruta).Cargar();

        Assert.NotNull(leido);
        Assert.Equal(new[] { "Finanzas", "Reportes" }, leido!.GruposAbiertos);
    }

    [Fact]
    public void Cargar_SinArchivo_DevuelveNullSinTirar()
    {
        Assert.Null(new ServicioPreferenciasSidebar(_ruta).Cargar());
    }

    [Fact]
    public void Cargar_ArchivoCorrupto_DevuelveNullSinTirar()
    {
        File.WriteAllText(_ruta, "{ esto no es json valido ");

        Assert.Null(new ServicioPreferenciasSidebar(_ruta).Cargar());
    }

    [Fact]
    public void Guardar_EnUnaRutaImposible_NoTira()
    {
        // Guardar se llama al abrir o cerrar un grupo: un fallo de IO no puede romper la
        // navegacion del usuario.
        var servicio = new ServicioPreferenciasSidebar(
            Path.Combine(Path.GetTempPath(), "no-existe-y-no-se-puede-crear\0", "x.json"));

        servicio.Guardar(new PreferenciasSidebar(new List<string> { "Finanzas" }));
    }

    [Fact]
    public void Guardar_ListaVacia_SeGuardaYSeLeeComoVacia()
    {
        // Todos los grupos cerrados es un estado legitimo, distinto de "nunca se guardo nada".
        var servicio = new ServicioPreferenciasSidebar(_ruta);
        servicio.Guardar(new PreferenciasSidebar(Array.Empty<string>()));

        var leido = new ServicioPreferenciasSidebar(_ruta).Cargar();

        Assert.NotNull(leido);
        Assert.Empty(leido!.GruposAbiertos);
    }
}
