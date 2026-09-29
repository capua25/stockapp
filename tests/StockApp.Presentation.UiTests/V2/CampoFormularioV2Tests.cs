using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;
using CampoFormularioV2 = StockApp.Presentation.Controls.V2.CampoFormulario;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Review Focus 2: CampoFormulario v2 agrega etiqueta y asterisco y NADA más. El mensaje de error
/// lo dibuja el DataValidationErrors del propio control con lo que ya produjo el seam
/// ErrorValidacionConverter (Themes/Controls.axaml). Si el componente interceptara la validación,
/// volverían a verse excepciones crudas de .NET.
/// </summary>
public class CampoFormularioV2Tests
{
    private const string CampoCantidad = """
        <v2:CampoFormulario x:Name="C" Etiqueta="Cantidad" Requerido="True">
            <TextBox x:Name="T" Text="{ReflectionBinding Cantidad, Mode=TwoWay}" />
        </v2:CampoFormulario>
        """;

    [AvaloniaFact]
    public void Etiqueta_12_500_ColorEtiquetaYAsteriscoRojoSiEsRequerido()
    {
        var campo = Buscar<CampoFormularioV2>(Montar(CampoCantidad), "C");
        var etiqueta = Parte<TextBlock>(campo, "PART_Etiqueta");
        var requerido = Parte<TextBlock>(campo, "PART_Requerido");

        Assert.Equal("Cantidad", etiqueta.Text);
        Assert.Equal(12, etiqueta.FontSize);
        Assert.Equal(FontWeight.Medium, etiqueta.FontWeight);
        Assert.Equal(Hex("#44403C"), ColorDe(etiqueta.Foreground));
        Assert.True(requerido.IsVisible);
        Assert.Equal("*", requerido.Text);
        Assert.Equal(Hex("#DC2626"), ColorDe(requerido.Foreground));
    }

    [AvaloniaFact]
    public void NoRequerido_OcultaElAsterisco()
    {
        var campo = Buscar<CampoFormularioV2>(Montar("""<v2:CampoFormulario x:Name="C" Etiqueta="Notas"><TextBox /></v2:CampoFormulario>"""), "C");

        Assert.False(Parte<TextBlock>(campo, "PART_Requerido").IsVisible);
    }

    [AvaloniaFact]
    public void ControlDeAdentro_ConservaElErrorConverterGlobal()
    {
        var entrada = Buscar<TextBox>(Montar(CampoCantidad), "T");

        Assert.NotNull(DataValidationErrors.GetErrorConverter(entrada));
    }

    [AvaloniaFact]
    public void TextoInvalidoEnDecimal_MuestraElMensajeDelSeamEnRojo()
    {
        var ventana = Montar(CampoCantidad);
        var modelo = new ModeloCampoDecimal { Cantidad = 5m };
        ventana.DataContext = modelo;
        Dispatcher.UIThread.RunJobs();
        var entrada = Buscar<TextBox>(ventana, "T");

        entrada.Text = "abc";
        Dispatcher.UIThread.RunJobs();

        var textos = ventana.GetVisualDescendants().OfType<TextBlock>().ToList();
        var mensaje = Assert.Single(textos, t => t.Text == "Ingresá un número válido.");
        Assert.Equal(Hex("#DC2626"), ColorDe(mensaje.Foreground));
        Assert.Equal(12, mensaje.FontSize);
        Assert.Contains(mensaje.GetVisualAncestors(), a => a is DataValidationErrors);
        Assert.DoesNotContain(textos, t => t.Text?.Contains("format", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Equal(5m, modelo.Cantidad);
    }
}
