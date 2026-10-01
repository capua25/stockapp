using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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
                xmlns:fin="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=GestionMunicipal"
                Width="1000" Height="700">
            <fin:NuevaImportacionView />
        </Window>
        """;

    private static async Task<(Window Window, DataGrid Grid, NuevaImportacionViewModel Vm)> MontarEnPasoRevisarAsync(
        GastoAnalizadoDto gasto, IReadOnlyList<FuenteFinanciamiento>? fuentesExistentes = null)
    {
        var service = new ImportacionServiceFake(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gasto },
            new List<LineaPoaAnalizadaDto>(),
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
        var seleccion = new ServicioSeleccionArchivoFake();
        var fuentes = new FuenteFinanciamientoServiceFake(fuentesExistentes ?? new List<FuenteFinanciamiento>());
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

    [AvaloniaFact]
    public async Task ComboBoxDeFuente_SeleccionaUnaFuenteExistente_LaFilaQuedaConElNombreNoConElToString()
    {
        // Review final F5d E2 (Critical C1): sin TextSearch.TextBinding, Avalonia 12 escribía
        // item.ToString() en el Text del ComboBox editable al SELECCIONAR un item existente (los
        // entities no overridean ToString) — la fila quedaba con "StockApp.Domain.Entities.
        // FuenteFinanciamiento" en vez del nombre real.
        var fuenteExistente = new FuenteFinanciamiento { Id = 1, Nombre = "Rentas Generales", Activo = true };
        var gasto = GastoBase(proveedor: "ACME SA", numeroFactura: "F-1", fuente: null);
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(gasto, fuentesExistentes: new[] { fuenteExistente });
        var fila = vm.FilasGasto[0];

        grid.SelectedItem = fila;
        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Fuente"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();

        var combo = window.GetVisualDescendants().OfType<ComboBox>().First();
        combo.SelectedItem = fuenteExistente;
        Dispatcher.UIThread.RunJobs();
        grid.CommitEdit();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Rentas Generales", fila.Fuente);
        Assert.DoesNotContain("StockApp.Domain.Entities", fila.Fuente);
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task FilaConErrorDeServidor_QuedaConBordeVisibleResaltado()
    {
        // Review final F5d E2 (Important I3): TieneErrorServidor no estaba bindeado a ningún
        // brush/borde — el mensaje del 400 decía "revisá las filas resaltadas" pero sólo había un
        // tooltip. Compara el BorderBrush ANTES vs DESPUÉS de marcar el error (no sólo el valor
        // final) para que el test detecte de verdad si el binding reacciona a TieneErrorServidor —
        // comparar sólo contra Brushes.Transparent al final sería un falso positivo si
        // BorderBrush nunca se hubiera bindeado (su default sin ningún Setter es null, que
        // también es "!= Transparent").
        var gasto = GastoBase(proveedor: "ACME SA", numeroFactura: "F-1", fuente: "Rentas Generales");
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(gasto);
        var fila = vm.FilasGasto[0];

        var row = window.GetVisualDescendants().OfType<DataGridRow>().First(r => ReferenceEquals(r.DataContext, fila));
        var colorSinError = Assert.IsAssignableFrom<ISolidColorBrush>(row.BorderBrush).Color;

        fila.AgregarErrorServidor("La fuente no existe en el catálogo.");
        Dispatcher.UIThread.RunJobs();

        var colorConError = Assert.IsAssignableFrom<ISolidColorBrush>(row.BorderBrush).Color;
        Assert.Equal(Colors.Transparent, colorSinError);
        Assert.NotEqual(Colors.Transparent, colorConError);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    // ── Formato es-UY (decisión 2026-10-01) ──────────────────────────────────

    private static GastoAnalizadoDto GastoConMonto(decimal? monto) => GastoBase("ACME SA", "F-1", "Rentas") with { Monto = monto };

    [AvaloniaFact]
    public async Task CeldaMonto_SeMuestraEnEsUy()
    {
        var original = System.Threading.Thread.CurrentThread.CurrentCulture;
        System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        try
        {
            var (window, _, vm) = await MontarEnPasoRevisarAsync(GastoConMonto(1500.5m));
            var fila = vm.FilasGasto[0];

            var textos = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => ReferenceEquals(t.DataContext, fila)).Select(t => t.Text).ToList();
            Assert.Contains("$ 1.500,50", textos);
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [AvaloniaFact]
    public async Task CeldaMontoFaltante_PuntoDecimal_SeRechazaConMensajeClaro_YComaSeAcepta()
    {
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(GastoConMonto(null));
        var fila = vm.FilasGasto[0];

        grid.SelectedItem = fila;
        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Monto"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();
        var caja = window.GetVisualDescendants().OfType<TextBox>()
            .First(t => ReferenceEquals(t.DataContext, fila) && t.IsEnabled && t.TemplatedParent is null);

        caja.Text = "5.4";
        Dispatcher.UIThread.RunJobs();
        Assert.Null(fila.Monto);
        Assert.Contains("Usá coma para los decimales: 5,4", DataValidationErrors.GetErrors(caja)!.Cast<object>());

        caja.Text = "1.500,50";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1500.50m, fila.Monto);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    // ── Confirmar bloqueado con errores de entrada (bug de integridad 2026-10-01) ──
    // La celda de Monto en edición con texto inválido nunca llega a la fila: sin bloqueo,
    // Confirmar importaba el monto ANTERIOR. Ver GuardarBloqueadoConErroresDeEntradaTests.

    private static TextBox EditarMonto(Window window, DataGrid grid, FilaGastoEditableVm fila)
    {
        grid.SelectedItem = fila;
        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Monto"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<TextBox>()
            .First(t => ReferenceEquals(t.DataContext, fila) && t.IsEnabled && t.TemplatedParent is null);
    }

    [AvaloniaFact]
    public async Task CeldaMonto_TextoInvalido_DeshabilitaConfirmar_YExplicaPorQue_YAlCorregirSeHabilita()
    {
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(GastoConMonto(1000m));
        var fila = vm.FilasGasto[0];
        var confirmar = window.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, vm.ConfirmarCommand));
        Assert.True(confirmar.IsEffectivelyEnabled);
        fila.DesbloquearCommand.Execute(null);   // el monto que vino de la planilla arranca con candado
        Dispatcher.UIThread.RunJobs();

        var caja = EditarMonto(window, grid, fila);
        caja.Text = "1000.5";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1000m, fila.Monto);
        Assert.False(confirmar.IsEffectivelyEnabled);
        Assert.False(vm.ConfirmarCommand.CanExecute(null));
        Assert.False(vm.PuedeConfirmar);
        Assert.Contains(StockApp.Presentation.ViewModels.ViewModelBase.MensajeErroresDeEntrada, vm.MensajeConfirmarBloqueado);

        caja.Text = "1000,5";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1000.5m, fila.Monto);
        Assert.True(confirmar.IsEffectivelyEnabled);
        Assert.Null(vm.MensajeConfirmarBloqueado);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// La grilla no puede "tragarse" el texto inválido: sin esto, terminar la edición (Enter,
    /// Tab, click en otra fila) descartaba el editor en rojo, la fila quedaba con el monto
    /// ANTERIOR, el error desaparecía y Confirmar se volvía a habilitar (verificado: commit=True,
    /// Monto=1000, PuedeConfirmar=True). Ahora el commit se cancela y la celda sigue en edición.
    /// </summary>
    [AvaloniaFact]
    public async Task CeldaMonto_TextoInvalido_TerminarLaEdicion_NoDescartaElErrorNiHabilitaConfirmar()
    {
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(GastoConMonto(1000m));
        var fila = vm.FilasGasto[0];
        fila.DesbloquearCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var caja = EditarMonto(window, grid, fila);
        caja.Text = "1000.5";
        Dispatcher.UIThread.RunJobs();

        var commit = grid.CommitEdit();
        Dispatcher.UIThread.RunJobs();

        Assert.False(commit);
        Assert.True(DataValidationErrors.GetHasErrors(caja));
        Assert.True(ArbolVisual.EsVisibleEnArbol(caja));
        Assert.False(vm.PuedeConfirmar);
        Assert.False(vm.ConfirmarCommand.CanExecute(null));

        caja.Text = "1000,5";
        Dispatcher.UIThread.RunJobs();
        Assert.True(grid.CommitEdit());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1000.5m, fila.Monto);
        Assert.True(vm.PuedeConfirmar);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
