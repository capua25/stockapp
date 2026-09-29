using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using StockApp.Presentation.Controls.V2;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

public class TarjetaContenidoEncabezadoTests
{
    [AvaloniaFact]
    public void TarjetaContenido_SuperficieBordeRadio10SombraUnicaPadding16()
    {
        var tarjeta = Buscar<TarjetaContenido>(Montar("""<v2:TarjetaContenido x:Name="T"><TextBlock Text="Hola" /></v2:TarjetaContenido>"""), "T");
        var marco = Parte<Border>(tarjeta, "PART_Marco");

        Assert.Equal(Hex("#FFFFFF"), ColorDe(marco.Background));
        Assert.Equal(Hex("#E7E5E4"), ColorDe(marco.BorderBrush));
        Assert.Equal(new Thickness(1), marco.BorderThickness);
        Assert.Equal(new CornerRadius(10), marco.CornerRadius);
        Assert.Equal(BoxShadows.Parse("0 1 2 0 #0D000000"), marco.BoxShadow);
        Assert.Equal(new Thickness(16), marco.Padding);
    }

    [AvaloniaFact]
    public void TarjetaContenido_PaddingPropio_SeRespeta()
    {
        var tarjeta = Buscar<TarjetaContenido>(Montar("""<v2:TarjetaContenido x:Name="T" Padding="24"><TextBlock Text="Hola" /></v2:TarjetaContenido>"""), "T");

        Assert.Equal(new Thickness(24), Parte<Border>(tarjeta, "PART_Marco").Padding);
    }

    [AvaloniaFact]
    public void TarjetaContenido_ControlNombradoAdentro_SeEncuentraDesdeLaVentana()
    {
        // ContentControl y no UserControl: el contenido queda en el NameScope de la vista, como
        // lo necesitan los code-behind que hacen FindControl.
        var ventana = Montar("""<v2:TarjetaContenido><TextBox x:Name="Adentro" /></v2:TarjetaContenido>""");

        Assert.NotNull(ventana.FindControl<TextBox>("Adentro"));
    }

    [AvaloniaFact]
    public void Encabezado_TituloYSubtituloConLaTipografiaDeLaSpec()
    {
        var encabezado = Buscar<EncabezadoPagina>(Montar("""<v2:EncabezadoPagina x:Name="E" Titulo="Productos" Subtitulo="128 productos activos" />"""), "E");
        var titulo = Parte<TextBlock>(encabezado, "PART_Titulo");
        var subtitulo = Parte<TextBlock>(encabezado, "PART_Subtitulo");

        Assert.Equal("Productos", titulo.Text);
        Assert.Equal(22, titulo.FontSize);
        Assert.Equal(FontWeight.SemiBold, titulo.FontWeight);
        Assert.Equal(26, titulo.LineHeight);
        Assert.Equal(Hex("#1C1917"), ColorDe(titulo.Foreground));
        Assert.Equal(13, subtitulo.FontSize);
        Assert.Equal(Hex("#57534E"), ColorDe(subtitulo.Foreground));
        Assert.Equal(new Thickness(0, 4, 0, 0), subtitulo.Margin);
        Assert.Equal(new Thickness(0, 0, 0, 16), encabezado.Margin);
    }

    [AvaloniaFact]
    public void Encabezado_SinBreadcrumbNiSubtitulo_NoLosMuestra()
    {
        var encabezado = Buscar<EncabezadoPagina>(Montar("""<v2:EncabezadoPagina x:Name="E" Titulo="Productos" />"""), "E");

        Assert.False(Parte<StackPanel>(encabezado, "PART_Breadcrumb").IsVisible);
        Assert.False(Parte<TextBlock>(encabezado, "PART_Subtitulo").IsVisible);
    }

    [AvaloniaFact]
    public void Encabezado_ClickEnElOrigenDelBreadcrumb_EjecutaElComando()
    {
        var ventana = Montar("""<v2:EncabezadoPagina x:Name="E" Titulo="Nuevo gasto" BreadcrumbOrigen="Gastos y facturas" BreadcrumbActual="Nuevo gasto" />""");
        var encabezado = Buscar<EncabezadoPagina>(ventana, "E");
        var comando = new ComandoContador();
        encabezado.BreadcrumbComando = comando;
        Dispatcher.UIThread.RunJobs();

        Clic(ventana, Parte<Button>(encabezado, "PART_BreadcrumbOrigen"));

        Assert.Equal(1, comando.Ejecuciones);
        Assert.Equal("Nuevo gasto", Parte<TextBlock>(encabezado, "PART_BreadcrumbActual").Text);
    }

    [AvaloniaFact]
    public void BreadcrumbOrigen_NoTomaElAltoDeBotonV2YPintaEnAcento()
    {
        // Review Focus 5: el link es un Button dentro de una vista .ui2; no puede recibir el alto
        // 36 ni el fondo de un botón.
        var encabezado = Buscar<EncabezadoPagina>(Montar("""<v2:EncabezadoPagina x:Name="E" Titulo="Nuevo gasto" BreadcrumbOrigen="Gastos y facturas" BreadcrumbActual="Nuevo gasto" />"""), "E");
        var origen = Parte<Button>(encabezado, "PART_BreadcrumbOrigen");

        Assert.True(origen.Bounds.Height < 24, $"alto {origen.Bounds.Height}");
        var presenter = Parte<ContentPresenter>(origen, "PART_ContentPresenter");
        Assert.Equal(Hex("#1D4ED8"), ColorDe(presenter.Foreground));
        Assert.Equal(FontWeight.Medium, presenter.FontWeight);
    }

    [AvaloniaFact]
    public void Encabezado_Acciones_QuedanAlineadasALaDerecha()
    {
        var ventana = Montar("""
            <v2:EncabezadoPagina x:Name="E" Titulo="Productos">
                <Button x:Name="Nuevo" Classes="primario" Content="Nuevo producto" />
            </v2:EncabezadoPagina>
            """);
        var encabezado = Buscar<EncabezadoPagina>(ventana, "E");
        var nuevo = Buscar<Button>(ventana, "Nuevo");

        var derecha = nuevo.TranslatePoint(new Point(nuevo.Bounds.Width, 0), encabezado)!.Value.X;

        Assert.Equal(encabezado.Bounds.Width, derecha, 1);
    }
}
