using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Optris.Icons.Avalonia;
using StockApp.Presentation.Controls.V2;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;
using EstadoVacioV2 = StockApp.Presentation.Controls.V2.EstadoVacio;
using TarjetaMetricaV2 = StockApp.Presentation.Controls.V2.TarjetaMetrica;

namespace StockApp.Presentation.UiTests.V2;

public class TarjetasV2Tests
{
    [AvaloniaFact]
    public void TarjetaMetrica_ConValor_20_600PrimarioYEtiqueta12Terciario()
    {
        var tarjeta = Buscar<TarjetaMetricaV2>(Montar("""<v2:TarjetaMetrica x:Name="M" Valor="3" Etiqueta="Facturas a vencer (7 días)" />"""), "M");
        var valor = Parte<TextBlock>(tarjeta, "PART_Valor");
        var etiqueta = Parte<TextBlock>(tarjeta, "PART_Etiqueta");

        Assert.True(valor.IsVisible);
        Assert.Equal("3", valor.Text);
        Assert.Equal(20, valor.FontSize);
        Assert.Equal(FontWeight.SemiBold, valor.FontWeight);
        Assert.Equal(Hex("#1C1917"), ColorDe(valor.Foreground));
        Assert.Equal(12, etiqueta.FontSize);
        Assert.Equal(Hex("#78716C"), ColorDe(etiqueta.Foreground));
        Assert.False(Parte<TextBlock>(tarjeta, "PART_SinDato").IsVisible);
        Assert.Equal(new CornerRadius(10), Parte<Border>(tarjeta, "PART_Marco").CornerRadius);
    }

    [AvaloniaFact]
    public void TarjetaMetrica_SinDato_MuestraGuionEnTextoGrupoYOcultaElValor()
    {
        var tarjeta = Buscar<TarjetaMetricaV2>(Montar("""<v2:TarjetaMetrica x:Name="M" Valor="99" SinDato="True" Etiqueta="Último backup" />"""), "M");
        var sinDato = Parte<TextBlock>(tarjeta, "PART_SinDato");

        Assert.False(Parte<TextBlock>(tarjeta, "PART_Valor").IsVisible);
        Assert.True(sinDato.IsVisible);
        Assert.Equal("—", sinDato.Text);
        Assert.Equal(Hex("#A8A29E"), ColorDe(sinDato.Foreground));
    }

    [AvaloniaFact]
    public void TarjetaAcceso_Click_EjecutaElComando()
    {
        var ventana = Montar("""<v2:TarjetaAcceso x:Name="A" Icono="mdi-package-variant" Titulo="Productos" Descripcion="Ver y administrar el catálogo" />""");
        var tarjeta = Buscar<TarjetaAcceso>(ventana, "A");
        var comando = new ComandoContador();
        tarjeta.Command = comando;
        Dispatcher.UIThread.RunJobs();

        Clic(ventana, tarjeta);

        Assert.Equal(1, comando.Ejecuciones);
    }

    [AvaloniaFact]
    public void TarjetaAcceso_MarcoIconoYTextos()
    {
        var tarjeta = Buscar<TarjetaAcceso>(Montar("""<v2:TarjetaAcceso x:Name="A" Icono="mdi-package-variant" Titulo="Productos" Descripcion="Ver y administrar el catálogo" />"""), "A");
        var marco = Parte<Border>(tarjeta, "PART_Marco");

        Assert.Equal(Hex("#FFFFFF"), ColorDe(marco.Background));
        Assert.Equal(Hex("#E7E5E4"), ColorDe(marco.BorderBrush));
        Assert.Equal(new CornerRadius(8), marco.CornerRadius);
        Assert.Equal(new Thickness(16), marco.Padding);
        Assert.Equal("mdi-package-variant", Parte<Icon>(tarjeta, "PART_Icono").Value);
        Assert.Equal(Hex("#1D4ED8"), ColorDe(Parte<Icon>(tarjeta, "PART_Icono").Foreground));
        Assert.Equal(13, Parte<TextBlock>(tarjeta, "PART_Titulo").FontSize);
        Assert.Equal(FontWeight.SemiBold, Parte<TextBlock>(tarjeta, "PART_Titulo").FontWeight);
        Assert.Equal(Hex("#78716C"), ColorDe(Parte<TextBlock>(tarjeta, "PART_Descripcion").Foreground));
    }

    [AvaloniaFact]
    public void TarjetaAcceso_Hover_NoUsaElVerdeViejo()
    {
        // Review Focus 5: TarjetaAcceso hereda de Button. El estilo viejo global
        // "Button:pointerover" (BrandSuave verde) no tiene que alcanzarla.
        var tarjeta = Buscar<TarjetaAcceso>(Montar("""<v2:TarjetaAcceso x:Name="A" Titulo="Productos" />"""), "A");

        ((IPseudoClasses)tarjeta.Classes).Set(":pointerover", true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#F5F5F4"), ColorDe(Parte<Border>(tarjeta, "PART_Marco").Background));
    }

    [AvaloniaFact]
    public void TarjetaAcceso_SinIcono_NoLoMuestra()
    {
        // Fix ronda 1: TarjetaAcceso.AplicarIcono ya no tiene guarda "if (hayIcono)" (su premisa
        // -que Optris lanza con clave nula- está refutada para Optris.Icons.Avalonia 12.0.7,
        // ver comentario en el código). Value siempre recibe Icono ?? string.Empty: acá se fija
        // que con Icono=null el control no lanza y el Value resultante queda vacío.
        var tarjeta = Buscar<TarjetaAcceso>(Montar("""<v2:TarjetaAcceso x:Name="A" Titulo="Reportes" />"""), "A");
        var icono = Parte<Icon>(tarjeta, "PART_Icono");

        Assert.False(icono.IsVisible);
        Assert.Equal(string.Empty, icono.Value);
    }

    [AvaloniaFact]
    public void TarjetaAcceso_IconoVacio_NoLoMuestraYNoLanza()
    {
        var tarjeta = Buscar<TarjetaAcceso>(Montar("""<v2:TarjetaAcceso x:Name="A" Icono="" Titulo="Reportes" />"""), "A");
        var icono = Parte<Icon>(tarjeta, "PART_Icono");

        Assert.False(icono.IsVisible);
        Assert.Equal(string.Empty, icono.Value);
    }

    [AvaloniaFact]
    public void TarjetaAcceso_SinIcono_TituloPegadoAlBordeIzquierdoDelContenido()
    {
        // Fix ronda 1 (F2): la columna de separación de 8px del Grid no puede quedar reservada
        // cuando no hay ícono -- si no, el título queda corrido del borde del contenido aunque
        // el Icon esté oculto.
        var tarjeta = Buscar<TarjetaAcceso>(Montar("""<v2:TarjetaAcceso x:Name="A" Titulo="Reportes" />"""), "A");

        Assert.Equal(0, XTituloRelativaAlContenido(tarjeta), 1);
    }

    [AvaloniaFact]
    public void TarjetaAcceso_ConIcono_TituloSeparado24DelBordeIzquierdoDelContenido()
    {
        // Con ícono la geometría no cambia: 16 (ancho del Icon, FontSize 16) + 8 (separación).
        var tarjeta = Buscar<TarjetaAcceso>(Montar("""<v2:TarjetaAcceso x:Name="A" Icono="mdi-package-variant" Titulo="Productos" />"""), "A");

        Assert.Equal(24, XTituloRelativaAlContenido(tarjeta), 1);
    }

    /// <summary>
    /// X del título relativa al borde interior del contenido: descuenta el BorderThickness Y el
    /// Padding del PART_Marco, porque TranslatePoint mide desde el borde EXTERIOR del Border.
    /// </summary>
    private static double XTituloRelativaAlContenido(TarjetaAcceso tarjeta)
    {
        var marco = Parte<Border>(tarjeta, "PART_Marco");
        var titulo = Parte<TextBlock>(tarjeta, "PART_Titulo");
        var xTitulo = titulo.TranslatePoint(new Point(0, 0), marco)!.Value.X;
        return xTitulo - marco.BorderThickness.Left - marco.Padding.Left;
    }

    [AvaloniaFact]
    public void EstadoVacio_Normal_Titulo15_600PrimarioYMensaje13Secundario()
    {
        var vacio = Buscar<EstadoVacioV2>(Montar("""<v2:EstadoVacio x:Name="V" Titulo="Sin resultados" Mensaje="No hay productos que coincidan." />"""), "V");
        var titulo = Parte<TextBlock>(vacio, "PART_Titulo");
        var mensaje = Parte<TextBlock>(vacio, "PART_Mensaje");

        Assert.Equal(15, titulo.FontSize);
        Assert.Equal(FontWeight.SemiBold, titulo.FontWeight);
        Assert.Equal(Hex("#1C1917"), ColorDe(titulo.Foreground));
        Assert.Equal(13, mensaje.FontSize);
        Assert.Equal(Hex("#57534E"), ColorDe(mensaje.Foreground));
        Assert.False(Parte<ContentPresenter>(vacio, "PART_Acciones").IsVisible);
    }

    [AvaloniaFact]
    public void EstadoVacio_EsError_TituloEnPeligroTexto()
    {
        // Conserva la semántica del EstadoVacio viejo: "no hay datos" vs "falló la carga".
        var vacio = Buscar<EstadoVacioV2>(Montar("""<v2:EstadoVacio x:Name="V" Titulo="No se pudo cargar" EsError="True" />"""), "V");

        Assert.Equal(Hex("#B91C1C"), ColorDe(Parte<TextBlock>(vacio, "PART_Titulo").Foreground));
    }

    [AvaloniaFact]
    public void EstadoVacio_ConAcciones_LasMuestra()
    {
        var ventana = Montar("""
            <v2:EstadoVacio x:Name="V" Titulo="No se pudo cargar" EsError="True">
                <Button x:Name="Reintentar" Classes="secundario" Content="Reintentar" />
            </v2:EstadoVacio>
            """);

        Assert.True(Parte<ContentPresenter>(Buscar<EstadoVacioV2>(ventana, "V"), "PART_Acciones").IsVisible);
        Assert.NotNull(ventana.FindControl<Button>("Reintentar"));
    }
}
