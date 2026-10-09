using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using StockApp.Presentation.Controls.V2;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

public class BarraFiltrosTests
{
    [AvaloniaFact]
    public void EscribirEnLaBusqueda_ActualizaElViewModelSinDeclararTwoWay()
    {
        var ventana = Montar("""<v2:BarraFiltros x:Name="F" TextoBusqueda="{ReflectionBinding Busqueda}" MarcaAguaBusqueda="Buscar por código o nombre" />""");
        var modelo = new ModeloBusqueda();
        ventana.DataContext = modelo;
        Dispatcher.UIThread.RunJobs();

        Parte<TextBox>(Buscar<BarraFiltros>(ventana, "F"), "PART_Busqueda").Text = "tornillo";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("tornillo", modelo.Busqueda);
    }

    [AvaloniaFact]
    public void CambioDeTextoBusqueda_LlegaALaCaja()
    {
        var barra = Buscar<BarraFiltros>(Montar("""<v2:BarraFiltros x:Name="F" />"""), "F");

        barra.TextoBusqueda = "pintura";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("pintura", Parte<TextBox>(barra, "PART_Busqueda").Text);
    }

    [AvaloniaFact]
    public void Busqueda_Ancho280Alto36ConMarcaDeAgua()
    {
        var barra = Buscar<BarraFiltros>(Montar("""<v2:BarraFiltros x:Name="F" MarcaAguaBusqueda="Buscar por código o nombre" />"""), "F");
        var busqueda = Parte<TextBox>(barra, "PART_Busqueda");

        Assert.Equal(280, busqueda.Bounds.Width);
        Assert.Equal(36, busqueda.Bounds.Height);
        Assert.Equal("Buscar por código o nombre", busqueda.PlaceholderText);
        Assert.Equal(new Thickness(0, 0, 0, 16), barra.Margin);
    }

    [AvaloniaFact]
    public void Filtros_VanDespuesDeLaBusquedaConSeparacion8()
    {
        var ventana = Montar("""
            <v2:BarraFiltros x:Name="F">
                <ComboBox x:Name="Categoria" Width="200" />
            </v2:BarraFiltros>
            """);
        var barra = Buscar<BarraFiltros>(ventana, "F");
        var categoria = Buscar<ComboBox>(ventana, "Categoria");

        Assert.Equal(288, categoria.TranslatePoint(new Point(0, 0), barra)!.Value.X);
    }

    [AvaloniaFact]
    public void SinBusqueda_NoMuestraElBuscadorYLosFiltrosArrancanALaIzquierda()
    {
        var ventana = Montar("""
            <v2:BarraFiltros x:Name="Barra" MostrarBusqueda="False"><Button x:Name="Filtro" Content="Buscar" /></v2:BarraFiltros>
            """, ancho: 800);
        var barra = Buscar<BarraFiltros>(ventana, "Barra");

        Assert.False(Parte<TextBox>(barra, "PART_Busqueda").IsVisible);
        var filtro = Buscar<Button>(ventana, "Filtro");
        Assert.Equal(0, filtro.TranslatePoint(default, barra)!.Value.X, 0.5);
    }

    [AvaloniaFact]
    public void PorDefecto_MuestraElBuscador()
    {
        var ventana = Montar("""<v2:BarraFiltros x:Name="Barra" />""");

        Assert.True(Parte<TextBox>(Buscar<BarraFiltros>(ventana, "Barra"), "PART_Busqueda").IsVisible);
    }
}
