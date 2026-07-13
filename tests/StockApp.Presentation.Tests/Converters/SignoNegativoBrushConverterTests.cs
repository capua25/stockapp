using System.Globalization;
using Avalonia;
using Avalonia.Media;
using StockApp.Presentation.Converters;
using Xunit;
// Alias porque StockApp.Application (namespace del proyecto de aplicación) colisiona con la
// clase Avalonia.Application.
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Tests.Converters;

/// <summary>
/// Verifica que <see cref="SignoNegativoBrushConverter"/> resalte en rojo los valores
/// negativos (ej. stock negativo en la grilla de Valorización) y deje el foreground default
/// (sin overridear, vía <see cref="AvaloniaProperty.UnsetValue"/>) para valores >= 0.
/// </summary>
public class SignoNegativoBrushConverterTests
{
    private static readonly SignoNegativoBrushConverter Sut = SignoNegativoBrushConverter.Instance;

    [Fact]
    public void Convert_Negativo_DevuelveBrushRojo()
    {
        var resultado = Sut.Convert(-6m, typeof(IBrush), null, CultureInfo.InvariantCulture);

        // Mismo rojo semántico que el token DangerBrush en Themes/Tokens.axaml (#DC2626).
        // En runtime el converter resuelve DangerBrush vía Application.Current.TryFindResource;
        // acá, sin Application inicializada, cae al fallback hardcodeado (ver
        // Convert_SinApplicationCurrent_CaeAlFallbackDeterministico).
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(resultado);
        Assert.Equal(Color.Parse("#DC2626"), brush.Color);
    }

    [Fact]
    public void Convert_SinApplicationCurrent_CaeAlFallbackDeterministico()
    {
        // Documenta el comportamiento determinista en esta suite: StockApp.Presentation.Tests
        // no tiene infraestructura Avalonia Headless, así que Application.Current es null acá.
        // No se puede montar una Application en este test (rompería el resto de la suite si
        // quedara un Application.Current global seteado); lo único verificable desde un test
        // xunit puro es que, en ausencia de Application, el converter no explota y devuelve
        // el fallback #DC2626 en vez de intentar resolver el token DangerBrush.
        Assert.Null(AvaloniaApp.Current);

        var resultado = Sut.Convert(-1m, typeof(IBrush), null, CultureInfo.InvariantCulture);

        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(resultado);
        Assert.Equal(Color.Parse("#DC2626"), brush.Color);
    }

    [Fact]
    public void Convert_Positivo_DevuelveUnsetValue()
    {
        var resultado = Sut.Convert(5m, typeof(IBrush), null, CultureInfo.InvariantCulture);

        Assert.Equal(AvaloniaProperty.UnsetValue, resultado);
    }

    [Fact]
    public void Convert_Cero_DevuelveUnsetValue()
    {
        var resultado = Sut.Convert(0m, typeof(IBrush), null, CultureInfo.InvariantCulture);

        Assert.Equal(AvaloniaProperty.UnsetValue, resultado);
    }

    [Fact]
    public void ConvertBack_NoSoportado_Lanza()
    {
        Assert.Throws<NotSupportedException>(
            () => Sut.ConvertBack(Brushes.Red, typeof(decimal), null, CultureInfo.InvariantCulture));
    }
}
