using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Optris.Icons.Avalonia;
using StockApp.Presentation.Controls.V2;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Ítem del sidebar v2 (spec §2.2 alto 32, §2.3 12/500, §4.1 pastilla activa).</summary>
public class ItemSidebarTests
{
    private static ItemSidebar Item(bool activo = false) =>
        Buscar<ItemSidebar>(Montar(
            $"""<v2:ItemSidebar x:Name="I" Icono="mdi-package-variant" Titulo="Productos" EstaActivo="{activo}" />"""), "I");

    [AvaloniaFact]
    public void Inactivo_Alto32SinFondoTextoEtiquetaIconoTerciario()
    {
        var item = Item();

        Assert.Equal(32, item.Bounds.Height);
        Assert.Equal(Colors.Transparent, ColorDe(Parte<Border>(item, "PART_Fondo").Background));
        Assert.Equal(new CornerRadius(8), Parte<Border>(item, "PART_Fondo").CornerRadius);
        var titulo = Parte<TextBlock>(item, "PART_Titulo");
        Assert.Equal("Productos", titulo.Text);
        Assert.Equal(12, titulo.FontSize);
        Assert.Equal(FontWeight.Medium, titulo.FontWeight);
        Assert.Equal(Hex("#44403C"), ColorDe(titulo.Foreground));
        var icono = Parte<Icon>(item, "PART_Icono");
        Assert.Equal("mdi-package-variant", icono.Value);
        Assert.Equal(Hex("#78716C"), ColorDe(icono.Foreground));
    }

    [AvaloniaFact]
    public void Activo_PastillaAcentoSuaveTextoAcentoOscuroIconoAcento()
    {
        var item = Item(activo: true);

        Assert.Equal(Hex("#DBEAFE"), ColorDe(Parte<Border>(item, "PART_Fondo").Background));
        Assert.Equal(Hex("#1E3A8A"), ColorDe(Parte<TextBlock>(item, "PART_Titulo").Foreground));
        Assert.Equal(Hex("#1D4ED8"), ColorDe(Parte<Icon>(item, "PART_Icono").Foreground));
    }

    [AvaloniaFact]
    public void CambiarEstaActivo_ActualizaLaPastilla()
    {
        // Ruling 6 del sidebar: EstaActivo cambia en cada navegación sin reconstruir el ítem.
        var item = Item();

        item.EstaActivo = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#DBEAFE"), ColorDe(Parte<Border>(item, "PART_Fondo").Background));
    }

    [AvaloniaFact]
    public void Hover_FondoNeutroYNoElVerdeViejo()
    {
        // Review Focus 5 de la Fase 0: un Button no puede tomar el "Button:pointerover" viejo.
        var item = Item();

        ((IPseudoClasses)item.Classes).Set(":pointerover", true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#F5F5F4"), ColorDe(Parte<Border>(item, "PART_Fondo").Background));
    }

    [AvaloniaFact]
    public void ActivoConHover_SigueEnLaPastillaActiva()
    {
        var item = Item(activo: true);

        ((IPseudoClasses)item.Classes).Set(":pointerover", true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#DBEAFE"), ColorDe(Parte<Border>(item, "PART_Fondo").Background));
    }

    [AvaloniaFact]
    public void Click_EjecutaElComando()
    {
        var ventana = Montar("""<v2:ItemSidebar x:Name="I" Titulo="Inicio" />""");
        var item = Buscar<ItemSidebar>(ventana, "I");
        var comando = new ComandoContador();
        item.Command = comando;

        Clic(ventana, item);

        Assert.Equal(1, comando.Ejecuciones);
    }
}
