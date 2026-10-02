using System.Globalization;
using System.Threading;
using Xunit;

namespace StockApp.Presentation.Tests.Arranque;

/// <summary>
/// La cultura por defecto del proceso del desktop (decisión 2026-10-01): es-UY construida desde
/// el punto de verdad FormatoEsUy, para que cualquier StringFormat o interpolación suelta quede
/// consistente aunque el SO esté en otro idioma.
/// </summary>
public class CulturaAppTests
{
    [Fact]
    public void Aplicar_FijaLaCulturaPorDefectoDelProcesoEnEsUy_AunqueElHiloArranqueEnEnUs()
    {
        var culturaHilo = CultureInfo.CurrentCulture;
        var culturaUiHilo = CultureInfo.CurrentUICulture;
        var defecto = CultureInfo.DefaultThreadCurrentCulture;
        var defectoUi = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            CulturaApp.Aplicar();

            Assert.Equal("26.400,00", 26400m.ToString("N2", CultureInfo.CurrentCulture));
            Assert.Equal("26.400,00", string.Format(CultureInfo.CurrentUICulture, "{0:N2}", 26400m));
            Assert.Equal("26.400,00", 26400m.ToString("N2", CultureInfo.DefaultThreadCurrentCulture));
            Assert.Equal("26.400,00", 26400m.ToString("N2", CultureInfo.DefaultThreadCurrentUICulture));

            // Un hilo nuevo (ej. continuaciones del pool) también la hereda.
            string? enOtroHilo = null;
            var hilo = new Thread(() => enOtroHilo = $"{1500.5m:N2}");
            hilo.Start();
            hilo.Join();
            Assert.Equal("1.500,50", enOtroHilo);
        }
        finally
        {
            CultureInfo.CurrentCulture = culturaHilo;
            CultureInfo.CurrentUICulture = culturaUiHilo;
            CultureInfo.DefaultThreadCurrentCulture = defecto;
            CultureInfo.DefaultThreadCurrentUICulture = defectoUi;
        }
    }
}
