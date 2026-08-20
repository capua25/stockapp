using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Configurador.Servicios;
using StockApp.Configurador.ViewModels;
using StockApp.Configurador.Views;
using Xunit;

namespace StockApp.Configurador.UiTests;

/// <summary>
/// Red mínima pedida en el review: NO es el banco headless completo de
/// StockApp.Presentation.UiTests, es el test justo para el modo de falla concreto — App.axaml
/// del Configurador carga sus propios Themes/Tokens.axaml y Controls.axaml vía
/// avares://GestionMunicipal.Configurador/..., y si ese nombre de assembly estuviera mal, la
/// ventana revienta en RUNTIME (compila perfecto). Ver TestAppBuilder: usa la clase App real,
/// no una reimplementación, así este test SÍ ejecuta el App.axaml real.
/// </summary>
public class MainWindowAbreTests
{
    private sealed class ProbadorConexionFake : IProbadorConexion
    {
        public Task<ResultadoPruebaConexion> ProbarAsync(string baseUrl, CancellationToken ct = default) =>
            Task.FromResult(ResultadoPruebaConexion.NoResponde);
    }

    [AvaloniaFact]
    public void MainWindow_AbreSinExcepcion_YElBotonPrimarioResuelveElTokenDeSuPropioTema()
    {
        var rutaConexionDeTest = Path.Combine(Path.GetTempPath(), "configurador-uitest-" + System.Guid.NewGuid() + ".json");
        var vm = new ConfiguradorViewModel(new ProbadorConexionFake(), rutaConexionDeTest);

        var window = new MainWindow { DataContext = vm };

        // Si App.Initialize() (invocado durante el Setup de TestAppBuilder, ANTES de este test)
        // no hubiera podido resolver avares://GestionMunicipal.Configurador/Themes/..., el
        // proceso de test ya habría fallado al arrancar. Mostrar la ventana y forzar el layout
        // real es lo que además obliga a MainWindow.axaml (con x:DataType compilado) a
        // construirse de punta a punta.
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Confirmación positiva de que los recursos realmente resolvieron (no solo "no explotó"):
        // Button.primary en Themes/Controls.axaml del Configurador fija Background al token
        // BrandHoverBrush (#FF15803D, definido en Themes/Tokens.axaml). Si el StyleInclude no
        // hubiera cargado, el botón quedaría con el estilo por defecto de FluentTheme (no este
        // verde), y esta aserción -no una excepción- sería la que lo detecte.
        var botonGuardar = window.GetVisualDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, "Guardar"));

        var fondo = Assert.IsType<SolidColorBrush>(botonGuardar.Background);
        Assert.Equal(Color.Parse("#15803D"), fondo.Color);
    }
}
