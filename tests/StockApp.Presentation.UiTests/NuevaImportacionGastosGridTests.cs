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
/// Tests headless de la grilla editable de Gastos (F5d Entrega 2 Task 7), calcados de
/// DataGridSortClickTests.cs y MovimientoFormControlValidacionTests.cs. Cubren automatizado lo
/// que una primera pasada de este plan dejaba solo como verificación orgánica manual: candado
/// por celda, ComboBox IsEditable con texto libre, y la regresión del bug
/// AvaloniaUI/Avalonia.Controls.DataGrid#232 (edición inline con DataGridCollectionView).
/// </summary>
public class NuevaImportacionGastosGridTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:fin="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=StockApp.Presentation"
                Width="1000" Height="700">
            <fin:NuevaImportacionView />
        </Window>
        """;

    private static async Task<(Window Window, DataGrid Grid, NuevaImportacionViewModel Vm)> MontarEnPasoRevisarAsync(GastoAnalizadoDto gasto)
    {
        var service = new ImportacionServiceFake(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gasto },
            new List<LineaPoaAnalizadaDto>(),
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
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

        var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "GridGastos");
        return (window, grid, vm);
    }

    private static GastoAnalizadoDto GastoBase(string? proveedor, string? numeroFactura, string? fuente) => new(
        HojaOrigen: "MARZO", NumeroFila: 1,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
        Proveedor: proveedor, ProveedorNuevo: false,
        NumeroFactura: numeroFactura, NumeroOrden: null,
        Detalle: "Compra", Destino: null,
        Fuente: fuente, FuenteDesconocida: fuente is null,
        CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
        LineaPoaAsignada: null);

    [AvaloniaFact]
    public async Task CeldaProveedorConValorCargado_QuedaBloqueada_CeldaFuenteFaltante_EsEditable()
    {
        var gasto = GastoBase(proveedor: "ACME SA", numeroFactura: "F-1", fuente: null);
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(gasto);
        var fila = vm.FilasGasto[0];

        grid.SelectedItem = fila;
        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Proveedor"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();
        var comboProveedor = window.GetVisualDescendants().OfType<ComboBox>().First();
        Assert.False(comboProveedor.IsEnabled);
        grid.CancelEdit();
        Dispatcher.UIThread.RunJobs();

        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Fuente"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();
        var comboFuente = window.GetVisualDescendants().OfType<ComboBox>().First();
        Assert.True(comboFuente.IsEnabled);
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task ComboBoxDeFuente_EsEditable_AceptaTextoLibre()
    {
        var gasto = GastoBase(proveedor: "ACME SA", numeroFactura: "F-1", fuente: null);
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(gasto);
        var fila = vm.FilasGasto[0];

        grid.SelectedItem = fila;
        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Fuente"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();

        var combo = window.GetVisualDescendants().OfType<ComboBox>().First();
        combo.Text = "Fuente Municipal Nueva";
        Dispatcher.UIThread.RunJobs();
        grid.CommitEdit();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Fuente Municipal Nueva", fila.Fuente);
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task EditarFactura_Commitea_SinPerderNiDuplicarLaFila()
    {
        var gasto = GastoBase(proveedor: "ACME SA", numeroFactura: null, fuente: "Rentas Generales");
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(gasto);
        Assert.Single(vm.FilasGasto);
        var fila = vm.FilasGasto[0];

        grid.SelectedItem = fila;
        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Factura"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();

        var texto = grid.GetVisualDescendants().OfType<TextBox>().First();
        texto.Text = "F-2026-001";
        Dispatcher.UIThread.RunJobs();
        grid.CommitEdit();
        Dispatcher.UIThread.RunJobs();

        // Regresión AvaloniaUI/Avalonia.Controls.DataGrid#232: el commit vía DataGridCollectionView
        // no debe perder ni duplicar la fila.
        Assert.Single(vm.FilasGasto);
        Assert.Same(fila, vm.FilasGasto[0]);
        Assert.Equal("F-2026-001", vm.FilasGasto[0].NumeroFactura);
    }
}
