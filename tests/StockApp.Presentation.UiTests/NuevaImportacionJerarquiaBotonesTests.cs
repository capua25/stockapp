using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Task 8.4 del plan de Fase B: NuevaImportacionView es un wizard (P8) con TRES botones primarios,
/// uno por paso mutuamente excluyente (Ruling B-18 punto 3). El invariante generico del guardian
/// (Vista_NoTieneUnSegundoBotonPrimario) NO aplica aca -- sin ViewModel real los 3 IsVisible de
/// PasoActual quedan sin resolver y caen al default true de la propiedad, dando un falso rojo
/// permanente. Este test reemplaza a ese invariante generico: monta la vista CON un ViewModel
/// real, recorre los 3 valores de PasoWizardImportacion y asserta que en cada paso hay
/// EXACTAMENTE UN boton primario visible, y que es el correcto.
/// </summary>
public class NuevaImportacionJerarquiaBotonesTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:fin="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=StockApp.Presentation"
                Width="1000" Height="700">
            <fin:NuevaImportacionView />
        </Window>
        """;

    private static (Window Window, NuevaImportacionViewModel Vm) Montar()
    {
        var service = new ImportacionServiceFake(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto>(),
            new List<LineaPoaAnalizadaDto>(),
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
        var seleccion = new ServicioSeleccionArchivoFake();
        var fuentes = new FuenteFinanciamientoServiceFake(new List<FuenteFinanciamiento>());
        var rubros = new RubroGastoServiceFake(new List<RubroGasto>());
        var proveedores = new ProveedorServiceFake(new List<Proveedor>());
        var lineasPoa = new LineaPoaServiceFake(new List<LineaPoa>());

        var vm = new NuevaImportacionViewModel(
            service, seleccion, new ConfirmacionServiceFake(), fuentes, rubros, proveedores, lineasPoa);

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    private static IReadOnlyList<Button> PrimariosVisibles(Window window)
        => window.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("primary") && ArbolVisual.EsVisibleEnArbol(b))
            .ToList();

    [AvaloniaTheory]
    [InlineData(PasoWizardImportacion.Cargar, "Analizar")]
    [InlineData(PasoWizardImportacion.Revisar, "Confirmar")]
    [InlineData(PasoWizardImportacion.Resultado, "Nueva importación")]
    public void CadaPaso_TieneExactamenteUnBotonPrimarioVisible_YEsElCorrecto(
        PasoWizardImportacion paso, string contenidoEsperado)
    {
        var (window, vm) = Montar();

        vm.PasoActual = paso;
        Dispatcher.UIThread.RunJobs();

        var primarios = PrimariosVisibles(window);

        Assert.Single(primarios);
        Assert.Equal(contenidoEsperado, primarios[0].Content);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
