using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using StockApp.Presentation.Controls.V2;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Deuda de la Fase 1 (spec §7): el alto de línea del título de página (token 26, el natural de Inter a 22
/// es 27) no tenía un test sobre un control real, como el que tiene la Alerta con su alto de 16.
/// </summary>
public class AltoDeLineaV2Tests
{
    [Theory]
    [InlineData(32.0, 16.0, 1.0, true)]
    [InlineData(15.79, 16.0, 1.33, true)]   // 133 %: 16 DIP son 21,28 px, redondeados a 21
    [InlineData(32.33, 16.0, 1.33, true)]   // 133 %: dos líneas, 42,56 px redondeados a 43
    [InlineData(27.0, 26.0, 1.0, false)]    // el alto natural de Inter a 22: un píxel de más es un defecto
    [InlineData(24.0, 16.0, 1.0, false)]    // línea y media
    [InlineData(0.0, 16.0, 1.0, false)]
    public void EsMultiploDeLinea_ToleraSoloElRedondeoDelEscalado(double alto, double altoLinea, double escala, bool esperado) =>
        Assert.Equal(esperado, EsMultiploDeLinea(alto, altoLinea, escala));

    [AvaloniaFact]
    public void EncabezadoPagina_TituloMideUnaLineaDelToken()
    {
        var encabezado = Buscar<EncabezadoPagina>(Montar("""<v2:EncabezadoPagina x:Name="E" Titulo="Productos" />"""), "E");
        var titulo = Parte<TextBlock>(encabezado, "PART_Titulo");

        Assert.Equal(26, titulo.LineHeight);
        Assert.True(EsMultiploDeLinea(titulo.Bounds.Height, 26, LayoutHelper.GetLayoutScale(titulo)),
            $"PART_Titulo mide {titulo.Bounds.Height}, no un múltiplo de 26.");
        Assert.Equal(1, Math.Round(titulo.Bounds.Height / 26));
    }

    [AvaloniaFact]
    public void TituloPaginaEnvuelto_CadaLineaMideElAltoDelToken()
    {
        var texto = Buscar<TextBlock>(Montar(
            """<TextBlock x:Name="T" Classes="titulo-pagina" Width="160" TextWrapping="Wrap" Text="¡Bienvenido, María Fernanda Rodríguez!" />"""),
            "T");

        Assert.Equal(26, texto.LineHeight);
        Assert.True(texto.Bounds.Height >= 52, $"El título no envolvió: alto {texto.Bounds.Height}.");
        Assert.True(EsMultiploDeLinea(texto.Bounds.Height, 26, LayoutHelper.GetLayoutScale(texto)),
            $"El título envuelto mide {texto.Bounds.Height}, no un múltiplo de 26.");
    }
}
