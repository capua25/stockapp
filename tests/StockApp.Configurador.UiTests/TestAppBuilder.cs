using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(StockApp.Configurador.UiTests.TestAppBuilder))]

namespace StockApp.Configurador.UiTests;

/// <summary>
/// A propósito NO reimplementa los ResourceInclude/StyleInclude de App.axaml a mano (a
/// diferencia de TestApp.cs en StockApp.Presentation.UiTests): usa la clase App REAL del
/// Configurador, así App.Initialize() ejecuta AvaloniaXamlLoader.Load(this) contra el App.axaml
/// REAL, con sus avares://GestionMunicipal.Configurador/Themes/Tokens.axaml y Controls.axaml
/// REALES. Si el nombre del assembly en esas URIs está mal escrito, la resolución del recurso
/// falla EN ESTE MISMO Setup — antes de que cualquier test corra — que es exactamente el modo
/// de falla que hay que cubrir (compila perfecto, revienta al abrir la ventana).
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<StockApp.Configurador.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
