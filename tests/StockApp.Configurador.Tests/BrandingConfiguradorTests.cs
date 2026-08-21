using System.Reflection;
using Xunit;

namespace StockApp.Configurador.Tests;

/// <summary>
/// AssemblyTitle es lo que Windows muestra como "Descripción del archivo" en las propiedades
/// del ejecutable (lo ve el usuario final al inspeccionar el .exe). Ningún .csproj lo definía,
/// así que el SDK de MSBuild lo derivaba de AssemblyName ("GestionMunicipal.Configurador", sin
/// espacio ni tilde) en vez de usar el Product ya correcto. Fix: StockApp.Configurador.csproj
/// ahora define &lt;AssemblyTitle&gt; explícito. NO se toca AssemblyName: sigue siendo
/// "GestionMunicipal.Configurador" a propósito (nombre del binario del instalador).
/// </summary>
public class BrandingConfiguradorTests
{
    [Fact]
    public void AssemblyTitle_TieneEspacioYTilde()
    {
        var titulo = typeof(App).Assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title;

        Assert.Equal("Gestión Municipal · Configurador de conexión", titulo);
    }
}
