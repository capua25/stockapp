using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Domain.Entities;
using StockApp.Presentation.Controls;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.Views.Catalogo;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// "Precio costo" del formulario de producto, con la View real (decisión 2026-10-01: es-UY en
/// toda la app). Antes bindeaba el <c>decimal</c> crudo: mostraba con la cultura AMBIENTE y
/// parseaba con el converter default de Avalonia (NumberStyles.Float), así que "5.4" se rechazaba
/// con el genérico y, en una máquina en-US, se ACEPTABA como 5,4 sin coma. La cultura ambiente se
/// fuerza a en-US para que el resultado no dependa de la máquina.
/// </summary>
public class ProductoFormPrecioCostoFormatoTests
{
    private static (Window Window, TextBox Caja, ProductoFormViewModel Vm) Montar()
    {
        var vm = new ProductoFormViewModel(
            new ProductoServiceFake(),
            new UnidadMedidaServiceFake(new List<UnidadMedida>()),
            new CategoriaServiceFake(new List<Categoria>()),
            new NavigationRecorderFake());

        var window = new Window { Width = 800, Height = 900, Content = new ProductoFormView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var campo = window.GetVisualDescendants().OfType<CampoFormulario>().Single(c => c.Etiqueta == "Precio costo");
        var caja = campo.GetVisualDescendants().OfType<TextBox>().First();
        return (window, caja, vm);
    }

    private static void ConEnUs(System.Action accion)
    {
        var original = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try { accion(); }
        finally { Thread.CurrentThread.CurrentCulture = original; }
    }

    [AvaloniaFact]
    public void PrecioCosto_SeMuestraEnEsUy()
    {
        ConEnUs(() =>
        {
            var (_, caja, vm) = Montar();

            vm.PrecioCosto = 1500.5m;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("1.500,5", caja.Text);
        });
    }

    [AvaloniaFact]
    public void PrecioCosto_MilesConPuntoYComaDecimal_SeAcepta()
    {
        ConEnUs(() =>
        {
            var (_, caja, vm) = Montar();

            caja.Text = "1.500,50";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1500.50m, vm.PrecioCosto);
            Assert.False(DataValidationErrors.GetHasErrors(caja));
        });
    }

    [AvaloniaFact]
    public void PrecioCosto_PuntoDecimal_SeRechazaConMensajeClaro()
    {
        ConEnUs(() =>
        {
            var (_, caja, vm) = Montar();
            vm.PrecioCosto = 7m;
            Dispatcher.UIThread.RunJobs();

            caja.Text = "5.4";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(7m, vm.PrecioCosto);
            Assert.Contains("Usá coma para los decimales: 5,4",
                DataValidationErrors.GetErrors(caja)!.Cast<object>());
        });
    }
}
