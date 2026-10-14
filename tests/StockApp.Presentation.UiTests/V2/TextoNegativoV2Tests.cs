using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Un número negativo FUERA de una grilla (los saldos del libro caja) se pinta con el peligro texto por token,
/// también sobre un título de sección. Dentro de una celda ya lo pinta DataGridV2.axaml.</summary>
public class TextoNegativoV2Tests
{
    private const string Contenido = """
        <StackPanel>
            <TextBlock x:Name="Suelto" Classes="negativo" Text="-$ 51.540,00" />
            <TextBlock x:Name="ConTitulo" Classes="titulo-seccion negativo" Text="-$ 51.540,00" />
            <TextBlock x:Name="Normal" Classes="titulo-seccion" Text="$ 2.639.460,00" />
        </StackPanel>
        """;

    [AvaloniaFact]
    public void FueraDeLaGrilla_ElNegativoEsPeligro_AunConTituloDeSeccion()
    {
        var ventana = MontajeV2.Montar(Contenido);

        Assert.Equal(MontajeV2.Hex("#B91C1C"), MontajeV2.ColorDe(MontajeV2.Buscar<TextBlock>(ventana, "Suelto").Foreground));
        Assert.Equal(MontajeV2.Hex("#B91C1C"), MontajeV2.ColorDe(MontajeV2.Buscar<TextBlock>(ventana, "ConTitulo").Foreground));
        Assert.Equal(MontajeV2.Hex("#1C1917"), MontajeV2.ColorDe(MontajeV2.Buscar<TextBlock>(ventana, "Normal").Foreground));
    }

    [AvaloniaFact]
    public void SinScopeUi2_NoSePinta()
    {
        var ventana = MontajeV2.Montar(Contenido, conScopeUi2: false);

        Assert.NotEqual(MontajeV2.Hex("#B91C1C"),
            MontajeV2.ColorDe(MontajeV2.Buscar<TextBlock>(ventana, "Suelto").Foreground ?? Avalonia.Media.Brushes.Black));
    }
}
