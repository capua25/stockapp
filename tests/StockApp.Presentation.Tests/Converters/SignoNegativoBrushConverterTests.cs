using System.Globalization;
using Avalonia;
using Avalonia.Media;
using StockApp.Presentation.Converters;
using Xunit;

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

        // Mismo rojo semántico que DangerBrush en Themes/Tokens.axaml (#DC2626): los
        // converters no pueden depender de Application.Current/FindResource (no hay
        // Application inicializada en los tests de esta suite), así que el color queda
        // hardcodeado en el converter en vez de resuelto vía DynamicResource.
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
