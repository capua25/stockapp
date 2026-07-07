using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(StockApp.Presentation.UiTests.TestApp))]

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Host headless minimo para el banco de pruebas de sort por click en DataGrid.
/// Carga el mismo FluentTheme + Fluent.xaml del DataGrid que usa la app real
/// (ver src/StockApp.Presentation/App.axaml), para reproducir fielmente el
/// comportamiento (temas/estilos) de DataGridColumnHeader.
/// </summary>
public class TestApp : Application
{
    public TestApp()
    {
        RequestedThemeVariant = ThemeVariant.Light;

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

        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://StockApp.Presentation.UiTests/"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
        });
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
