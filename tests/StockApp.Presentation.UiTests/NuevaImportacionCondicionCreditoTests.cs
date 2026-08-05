using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Cierra un hueco puntual pedido por el encargo de verificacion de UI: "que exista control para
/// marcar credito/condicion de pago" en NuevaImportacionView, calcado del bug historico de
/// IngresoPorFacturaView donde faltaba exactamente ese control. La columna "Condicion" (ComboBox)
/// y "Vencimiento" (CalendarDatePicker condicional) existen en el axaml
/// (NuevaImportacionView.axaml lineas ~248-279) pero NINGUN test headless ejercitaba esa columna
/// todavia -- NuevaImportacionGastosGridTests.cs solo cubre Proveedor/Fuente/Rubro/Factura. Mismo
/// patron de montaje que esos tests (MontarEnPasoRevisarAsync).
/// </summary>
public class NuevaImportacionCondicionCreditoTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:fin="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=StockApp.Presentation"
                Width="1000" Height="700">
            <fin:NuevaImportacionView />
        </Window>
        """;

    private static async System.Threading.Tasks.Task<(Window Window, DataGrid Grid, NuevaImportacionViewModel Vm)> MontarEnPasoRevisarAsync(
        GastoAnalizadoDto gasto, IReadOnlyList<Proveedor>? proveedoresExistentes = null)
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
        var proveedores = new ProveedorServiceFake(proveedoresExistentes ?? new List<Proveedor>());
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

    private static GastoAnalizadoDto GastoContado() => new(
        HojaOrigen: "MARZO", NumeroFila: 1,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
        Proveedor: "ACME SA", ProveedorNuevo: false,
        NumeroFactura: "F-1", NumeroOrden: null,
        Detalle: "Compra", Destino: null,
        Fuente: "Rentas Generales", FuenteDesconocida: false,
        CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
        LineaPoaAsignada: null); // sin linea POA -> heuristica de FilaGastoEditableVm.Desde arranca en Contado.

    [AvaloniaFact]
    public async System.Threading.Tasks.Task ColumnaCondicion_ExisteYElComboOfreceContadoYCredito()
    {
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(GastoContado());
        var fila = vm.FilasGasto[0];
        Assert.Equal(CondicionPago.Contado, fila.Condicion);

        var columnaCondicion = grid.Columns.FirstOrDefault(c => Equals(c.Header, "Condición"));
        Assert.NotNull(columnaCondicion);

        grid.SelectedItem = fila;
        grid.CurrentColumn = columnaCondicion;
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();

        var combo = window.GetVisualDescendants().OfType<ComboBox>().First();
        var opciones = combo.ItemsSource!.Cast<CondicionPago>().ToList();
        Assert.Contains(CondicionPago.Contado, opciones);
        Assert.Contains(CondicionPago.Credito, opciones);

        combo.SelectedItem = CondicionPago.Credito;
        Dispatcher.UIThread.RunJobs();
        grid.CommitEdit();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(CondicionPago.Credito, fila.Condicion);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// El otro control historicamente faltante (marcar credito): Vencimiento debe habilitarse
    /// SOLO cuando Condicion pasa a Credito (axaml linea ~276, IsEnabled con ObjectConverters.Equal
    /// contra CondicionPago.Credito) -- se verifica el efecto real sobre el control, no solo el
    /// valor de la propiedad Condicion.
    /// </summary>
    [AvaloniaFact]
    public async System.Threading.Tasks.Task CambiarCondicionACredito_HabilitaVencimiento_CambiarAContado_LoDeshabilitaYLimpia()
    {
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(GastoContado());
        var fila = vm.FilasGasto[0];

        var columnaVencimiento = grid.Columns.First(c => Equals(c.Header, "Vencimiento"));
        grid.SelectedItem = fila;
        grid.CurrentColumn = columnaVencimiento;
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();

        var pickerVencimiento = window.GetVisualDescendants().OfType<CalendarDatePicker>().First();
        Assert.False(pickerVencimiento.IsEnabled); // Contado: no debe poder cargar vencimiento.

        grid.CancelEdit();
        Dispatcher.UIThread.RunJobs();

        fila.Condicion = CondicionPago.Credito;
        Dispatcher.UIThread.RunJobs();

        grid.CurrentColumn = columnaVencimiento;
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();

        var pickerHabilitado = window.GetVisualDescendants().OfType<CalendarDatePicker>().First();
        Assert.True(pickerHabilitado.IsEnabled);

        grid.CancelEdit();
        Dispatcher.UIThread.RunJobs();

        fila.FechaVencimiento = new System.DateOnly(2026, 4, 1);
        fila.Condicion = CondicionPago.Contado;
        Dispatcher.UIThread.RunJobs();

        Assert.Null(fila.FechaVencimiento); // OnCondicionChanged limpia el vencimiento al volver a Contado.

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Simetria con NuevaImportacionGastosGridTests.ComboBoxDeFuente_SeleccionaUnaFuenteExistente_LaFilaQuedaConElNombreNoConElToString:
    /// el combo de Proveedor tiene el MISMO riesgo (TextSearch.TextBinding) que el de Fuente, pero
    /// ese caso puntual no tenia test propio todavia.
    /// </summary>
    [AvaloniaFact]
    public async System.Threading.Tasks.Task ComboBoxDeProveedor_SeleccionaUnProveedorExistente_LaFilaQuedaConElNombreNoConElToString()
    {
        var proveedorExistente = new Proveedor { Id = 1, Nombre = "Ferreteria Central", Activo = true };
        var gasto = GastoContado() with { Proveedor = null, ProveedorNuevo = false };
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(gasto, proveedoresExistentes: new[] { proveedorExistente });
        var fila = vm.FilasGasto[0];

        grid.SelectedItem = fila;
        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Proveedor"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();

        var combo = window.GetVisualDescendants().OfType<ComboBox>().First();
        combo.SelectedItem = proveedorExistente;
        Dispatcher.UIThread.RunJobs();
        grid.CommitEdit();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Ferreteria Central", fila.Proveedor);
        Assert.DoesNotContain("StockApp.Domain.Entities", fila.Proveedor);
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
