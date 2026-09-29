using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Review Focus 1: fuera de .ui2 (todas las vistas de hoy) los controles se ven EXACTAMENTE
/// como antes de la Fase 0. Valores tomados de Themes/Tokens.axaml y Themes/Controls.axaml viejos.
/// </summary>
public class ControlesV2ConvivenciaTests
{
    [AvaloniaFact]
    public void BotonPrimaryViejo_FueraDeUi2_SigueVerde()
    {
        var boton = Buscar<Button>(Montar("""<Button x:Name="B" Classes="primary" Content="Guardar" />""", conScopeUi2: false), "B");

        Assert.Equal(Hex("#15803D"), ColorDe(boton.Background));
        Assert.Equal(new CornerRadius(6), boton.CornerRadius);
    }

    [AvaloniaFact]
    public void BotonGhostViejo_FueraDeUi2_SigueAlineadoALaIzquierda()
    {
        var boton = Buscar<Button>(Montar("""<Button x:Name="B" Classes="ghost" Content="Inicio" />""", conScopeUi2: false), "B");

        Assert.Equal(HorizontalAlignment.Left, boton.HorizontalContentAlignment);
        Assert.Equal(new Thickness(12, 8), boton.Padding);
        Assert.Equal(new Thickness(0), boton.BorderThickness);
        Assert.Equal(Hex("#0F172A"), ColorDe(boton.Foreground));
    }

    [AvaloniaFact]
    public void TextBoxViejo_FueraDeUi2_SigueConRadio6YBordeViejo()
    {
        var entrada = Buscar<TextBox>(Montar("""<TextBox x:Name="T" />""", conScopeUi2: false), "T");

        Assert.Equal(new CornerRadius(6), entrada.CornerRadius);
        Assert.Equal(Hex("#E2E8F0"), ColorDe(entrada.BorderBrush));
        Assert.Equal(new Thickness(10, 8), entrada.Padding);
    }

    [AvaloniaFact]
    public void ClaseV2FueraDeUi2_NoRecibeEstiloV2()
    {
        var boton = Buscar<Button>(Montar("""<Button x:Name="B" Classes="primario" Content="Guardar" />""", conScopeUi2: false), "B");

        Assert.NotEqual(Hex("#1D4ED8"), ColorDe(boton.Background));
        Assert.True(double.IsNaN(boton.Height));
    }

    [AvaloniaFact]
    public void TituloPaginaFueraDeUi2_NoRecibeEstiloV2()
    {
        var texto = Buscar<TextBlock>(Montar("""<TextBlock x:Name="T" Classes="titulo-pagina" Text="Productos" />""", conScopeUi2: false), "T");

        Assert.NotEqual(22, texto.FontSize);
    }
}
