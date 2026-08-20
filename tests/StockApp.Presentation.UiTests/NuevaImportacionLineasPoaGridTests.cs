using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
/// Tests headless de la grilla editable de Líneas POA (F5d Entrega 2 Task 8), mismo criterio que
/// NuevaImportacionGastosGridTests.cs (Task 7): cubren automatizado el combo de Programa gateado
/// por EsNueva y la regresión del bug AvaloniaUI/Avalonia.Controls.DataGrid#232 sobre esta grilla.
/// </summary>
public class NuevaImportacionLineasPoaGridTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:fin="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=GestionMunicipal"
                Width="1000" Height="700">
            <fin:NuevaImportacionView />
        </Window>
        """;

    private static async Task<(Window Window, DataGrid Grid, NuevaImportacionViewModel Vm)> MontarEnPasoRevisarAsync(LineaPoaAnalizadaDto linea)
    {
        var service = new ImportacionServiceFake(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto>(),
            new List<LineaPoaAnalizadaDto> { linea },
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

        await vm.SeleccionarGastosCommand.ExecuteAsync(null);
        await vm.SeleccionarPoaCommand.ExecuteAsync(null);
        await vm.AnalizarCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        // El TabControl sólo realiza visualmente el contenido de la pestaña seleccionada (Gastos,
        // índice 0, por defecto) — sin esto GridLineasPoa no aparece en GetVisualDescendants().
        var tabControl = window.GetVisualDescendants().OfType<TabControl>().First();
        tabControl.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();

        var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "GridLineasPoa");
        return (window, grid, vm);
    }

    private static LineaPoaAnalizadaDto LineaBase(bool esNueva) => new(
        Hoja: "COMPOSTERAS", Ejercicio: 2026, EsNueva: esNueva,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Literal: "C", FuenteDesconocida: false, Presupuesto: 1000m, SaldoPlanilla: 1000m,
        Movimientos: new List<MovimientoPoaAnalizadoDto>());

    [AvaloniaFact]
    public async Task LineaExistente_ProgramaNoEsVisible_LineaNueva_ProgramaEsEditable()
    {
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(LineaBase(esNueva: false));
        var fila = vm.FilasLineaPoa[0];

        grid.SelectedItem = fila;
        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Programa"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();

        // Línea existente: EsNueva=false, el combo de Programa no debe quedar visible/editable.
        // GetVisualDescendants() igual devuelve el ComboBox de la CellEditingTemplate (IsVisible=False
        // no lo saca del árbol visual, sólo lo oculta) — el ajuste real detectado al correr este test
        // headless es afirmar IsVisible/IsEnabled en vez de ausencia, mismo criterio que
        // NuevaImportacionGastosGridTests (IsEnabled, no ausencia, para el candado de Proveedor).
        var combo = window.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault();
        Assert.True(combo is null || !combo.IsVisible);
    }

    [AvaloniaFact]
    public async Task LineaNueva_ProgramaEsEditable_CommiteaSinPerderLaFila()
    {
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(LineaBase(esNueva: true));
        Assert.Single(vm.FilasLineaPoa);
        var fila = vm.FilasLineaPoa[0];

        grid.SelectedItem = fila;
        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Programa"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();

        var combo = window.GetVisualDescendants().OfType<ComboBox>().First();
        Assert.True(combo.IsEnabled);
        combo.Text = "Compostaje comunitario";
        Dispatcher.UIThread.RunJobs();
        grid.CommitEdit();
        Dispatcher.UIThread.RunJobs();

        // Regresión AvaloniaUI/Avalonia.Controls.DataGrid#232: el commit vía DataGridCollectionView
        // no debe perder ni duplicar la fila.
        Assert.Single(vm.FilasLineaPoa);
        Assert.Equal("Compostaje comunitario", vm.FilasLineaPoa[0].Programa);
    }
}
