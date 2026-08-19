using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.MaterialDesign;

[assembly: AvaloniaTestApplication(typeof(StockApp.Presentation.UiTests.TestApp))]

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Host headless minimo para el banco de pruebas de sort por click en DataGrid, y tambien para
/// MovimientoFormControlValidacionTests.cs (bug de InvalidCastException en "Precio unitario" +
/// politica de DataValidationErrors.ErrorConverter). Carga el mismo FluentTheme + Fluent.xaml
/// del DataGrid que usa la app real (ver src/StockApp.Presentation/App.axaml), y ADEMAS
/// Tokens/Typography/Controls del StockApp UI Kit real (mismo orden que App.axaml: FluentTheme
/// primero, Controls.axaml despues para poder overridear), para reproducir fielmente el
/// comportamiento real de estilos/recursos (DataGridColumnHeader, TextBox, DataValidationErrors).
/// </summary>
public class TestApp : Avalonia.Application
{
    // IconProvider.Current es un singleton ESTATICO de proceso (Optris.Icons.Avalonia.IconProvider),
    // pero Avalonia.Headless.XUnit reconstruye un TestApp NUEVO por cada [AvaloniaFact] (aislamiento
    // por test). Registrar sin guardia hacia que el SEGUNDO test de cualquier clase intentara
    // registrar el prefijo "mdi" de nuevo -> IconProvider.Register lanza ArgumentException interna
    // ("Prefix... conflicts with existing...") durante la construccion de la Application, que
    // Avalonia.Headless.XUnit no propaga como fallo de test sino que cuelga el dispatcher del host
    // headless para SIEMPRE (confirmado con ilspycmd contra Optris.Icons.Avalonia.dll: Register no
    // es idempotente). Guardia estatica para registrar una sola vez por proceso, igual que Program.cs
    // en produccion (que solo construye la Application una vez).
    private static bool _iconProviderRegistrado;

    public TestApp()
    {
        RequestedThemeVariant = ThemeVariant.Light;

        // Registro del proveedor de íconos (mismo que Program.cs en producción): sin esto,
        // cualquier <i:Icon Value="mdi-..."/> (usado por ej. en NuevaImportacionView.axaml para el
        // candado de celda bloqueada) tira KeyNotFoundException "No IIconProvider with prefix
        // matching..." al construir el layout de la celda — Task 7 F5d E2.
        if (!_iconProviderRegistrado)
        {
            IconProvider.Current.Register<MaterialDesignIconProvider>();
            _iconProviderRegistrado = true;
        }

        // Workaround puntual del banco de pruebas: el tema Fluent del DataGrid resuelve
        // DataGridRowBackgroundBrush/DataGridCellBackgroundBrush/DataGridCurrencyVisualPrimaryBrush/
        // DataGridFillerColumnGridLinesBrush como StaticResource diferido apuntando a
        // "SystemControlTransparentBrush" (definido en Avalonia.Themes.Fluent/Accents/BaseResources.xaml).
        // En este host headless minimalista (sin AvaloniaXamlLoader.Load de un App.axaml real como en
        // produccion) esa resolucion diferida de StaticResource no encuentra el recurso a tiempo durante
        // el primer layout del DataGrid y tira KeyNotFoundException. Se definen las 4 claves finales
        // directamente para que el lookup las encuentre en Application.Resources antes de intentar
        // construir el valor diferido del tema del DataGrid. Esto es irrelevante para lo que este banco
        // de pruebas necesita verificar (el click de sort), no afecta el codigo de produccion.
        Resources["DataGridRowBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
        Resources["DataGridCellBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
        Resources["DataGridCurrencyVisualPrimaryBrush"] = new SolidColorBrush(Colors.Transparent);
        Resources["DataGridFillerColumnGridLinesBrush"] = new SolidColorBrush(Colors.Transparent);

        Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(
            new Uri("avares://StockApp.Presentation.UiTests/"))
        {
            Source = new Uri("avares://StockApp.Presentation/Themes/Tokens.axaml")
        });

        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://StockApp.Presentation.UiTests/"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
        });

        // StockApp UI Kit real, mismo orden que App.axaml (despues del FluentTheme, para poder
        // overridear): Controls.axaml es donde vive la politica de DataValidationErrors.ErrorConverter.
        Styles.Add(new StyleInclude(new Uri("avares://StockApp.Presentation.UiTests/"))
        {
            Source = new Uri("avares://StockApp.Presentation/Themes/Typography.axaml")
        });
        Styles.Add(new StyleInclude(new Uri("avares://StockApp.Presentation.UiTests/"))
        {
            Source = new Uri("avares://StockApp.Presentation/Themes/Controls.axaml")
        });

        // DESPUES del StyleInclude del Fluent.xaml del DataGrid y de Controls.axaml, mismo orden
        // exacto que App.axaml. Sin esto, las grillas se testean contra Fluent crudo. El guardian
        // de este include llega con el estilo real de grilla (tanda 2): hoy no hay nada observable
        // que distinga cargarlo de no cargarlo.
        Styles.Add(new StyleInclude(new Uri("avares://StockApp.Presentation.UiTests/"))
        {
            Source = new Uri("avares://StockApp.Presentation/Themes/DataGrid.axaml")
        });
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
