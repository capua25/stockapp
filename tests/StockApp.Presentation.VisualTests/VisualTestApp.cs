using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.MaterialDesign;
using StockApp.Presentation;

[assembly: AvaloniaTestApplication(typeof(StockApp.Presentation.VisualTests.VisualTestApp))]

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Host headless con render REAL (Skia): lo que se captura es lo que dibujaría la app. Carga los
/// mismos includes que App.axaml, en el mismo orden (custodiado por IncludesAppAxamlTests).
/// </summary>
public class VisualTestApp : Avalonia.Application
{
    // Mismo guard que UiTests/TestAppBuilder.cs: IconProvider.Register no es idempotente y
    // Avalonia.Headless.XUnit construye una Application por test.
    private static bool _iconProviderRegistrado;

    public static readonly string[] ResourceIncludes =
    [
        "avares://GestionMunicipal/Themes/Tokens.axaml",
        "avares://GestionMunicipal/Themes/V2/Tokens.axaml",
        "avares://GestionMunicipal/Controls/Componentes.axaml",
        "avares://GestionMunicipal/Controls/V2/ComponentesV2.axaml",
    ];

    public static readonly string[] StyleIncludes =
    [
        "avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml",
        "avares://GestionMunicipal/Themes/Typography.axaml",
        "avares://GestionMunicipal/Themes/Controls.axaml",
        "avares://GestionMunicipal/Themes/DataGrid.axaml",
        "avares://GestionMunicipal/Themes/V2/ControlesV2.axaml",
    ];

    public VisualTestApp()
    {
        RequestedThemeVariant = ThemeVariant.Light;

        // F1 (review): App.axaml registra ViewLocator en Application.DataTemplates (única
        // fuente de verdad, ver App.axaml). Sin este registro, cualquier ContentControl cuyo
        // Content sea un ViewModel real (MainWindow, ShellMainView, GastoFormView, etc.) renderiza
        // el placeholder "Not Found: ..." de ViewLocator en vez de la vista real, y ninguna
        // comparación visual lo detecta. Custodiado por
        // ViewLocatorTests.ResuelveLaVistaRealParaUnViewModelDeProduccion_NoElPlaceholder.
        DataTemplates.Add(new ViewLocator());

        if (!_iconProviderRegistrado)
        {
            IconProvider.Current.Register<MaterialDesignIconProvider>();
            _iconProviderRegistrado = true;
        }

        // Mismo workaround que UiTests/TestAppBuilder.cs: sin App.axaml real, el tema del DataGrid
        // no resuelve a tiempo SystemControlTransparentBrush. En producción valen Transparent.
        Resources["DataGridRowBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
        Resources["DataGridCellBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
        Resources["DataGridCurrencyVisualPrimaryBrush"] = new SolidColorBrush(Colors.Transparent);
        Resources["DataGridFillerColumnGridLinesBrush"] = new SolidColorBrush(Colors.Transparent);

        foreach (var fuente in ResourceIncludes)
        {
            Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://StockApp.Presentation.VisualTests/"))
            {
                Source = new Uri(fuente)
            });
        }

        Styles.Add(new FluentTheme());

        foreach (var fuente in StyleIncludes)
        {
            Styles.Add(new StyleInclude(new Uri("avares://StockApp.Presentation.VisualTests/"))
            {
                Source = new Uri(fuente)
            });
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<VisualTestApp>()
            .UseSkia()
            .WithInterFont()
            .WithDataAnnotationsValidation()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
