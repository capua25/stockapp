using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StockApp.Application.Authorization;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Application.Catalogo;
using StockApp.Presentation.Views.Dialogs;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Diálogo v2 de "Recalcular stock": se prueba el diálogo real (x:Name y Click), no el VM. Un enganche perdido
/// deja "Recalcular" deshabilitado para siempre o no devuelve el producto elegido.
/// </summary>
public class RecalcularStockDialogTests
{
    private static ProductoDto P(int id, string nombre) => new(id, $"C-{id}", null, nombre, null, 1, "Construcción", null, 1,
        "Unidad", 100m, 5m, 0m, true, new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc));

    private static readonly Func<string?, CancellationToken, Task<IEnumerable<object>>> SinResultados =
        (_, _) => Task.FromResult<IEnumerable<object>>(Array.Empty<object>());

    private static Task<T> Abrir<T>(Window dialogo)
    {
        var duenio = new Window();
        duenio.Show();
        var resultado = dialogo.ShowDialog<T>(duenio);
        Dispatcher.UIThread.RunJobs();
        return resultado;
    }

    private static void Clic(Window dialogo, string boton)
    {
        dialogo.FindControl<Button>(boton)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void SinProductoElegido_RecalcularDeshabilitado()
    {
        var dialogo = new RecalcularStockDialog(SinResultados);
        Abrir<ProductoDto?>(dialogo);

        Assert.False(dialogo.FindControl<Button>("RecalcularButton")!.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void ConProductoElegido_RecalcularDevuelveEseProducto()
    {
        var dialogo = new RecalcularStockDialog(SinResultados);
        var resultado = Abrir<ProductoDto?>(dialogo);
        var elegido = P(42, "Cemento portland 50 kg");

        dialogo.FindControl<AutoCompleteBox>("BuscadorProducto")!.SelectedItem = elegido;
        Dispatcher.UIThread.RunJobs();
        // Clic levanta el evento aunque el botón esté deshabilitado: lo que prueba el gate es el estado, no el cierre.
        Assert.True(dialogo.FindControl<Button>("RecalcularButton")!.IsEffectivelyEnabled);
        Clic(dialogo, "RecalcularButton");

        Assert.True(resultado.IsCompleted);
        Assert.Same(elegido, resultado.Result);
    }

    [AvaloniaFact]
    public void EditarElTextoDespuesDeElegir_DeshabilitaRecalcular()
    {
        var dialogo = new RecalcularStockDialog(SinResultados);
        Abrir<ProductoDto?>(dialogo);
        var buscador = dialogo.FindControl<AutoCompleteBox>("BuscadorProducto")!;
        buscador.SelectedItem = P(42, "Cemento portland 50 kg");
        Dispatcher.UIThread.RunJobs();
        Assert.True(dialogo.FindControl<Button>("RecalcularButton")!.IsEffectivelyEnabled);

        buscador.Text = "Arena";
        Dispatcher.UIThread.RunJobs();

        Assert.False(dialogo.FindControl<Button>("RecalcularButton")!.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Cancelar_DevuelveNull()
    {
        var dialogo = new RecalcularStockDialog(SinResultados);
        var resultado = Abrir<ProductoDto?>(dialogo);
        dialogo.FindControl<AutoCompleteBox>("BuscadorProducto")!.SelectedItem = P(1, "Uno");

        Clic(dialogo, "CancelarButton");

        Assert.Null(resultado.Result);
    }

    [AvaloniaFact]
    public void ElBuscadorUsaElDelVm()
    {
        var dialogo = new RecalcularStockDialog(SinResultados);

        Assert.Same(SinResultados, dialogo.FindControl<AutoCompleteBox>("BuscadorProducto")!.AsyncPopulator);
    }

    [AvaloniaFact]
    public void Textos_YBotonesSinRecorteDentroDelAnchoUtil()
    {
        var dialogo = new RecalcularStockDialog(SinResultados);
        Abrir<ProductoDto?>(dialogo);

        Assert.Equal("Recalcular stock", dialogo.Title);
        Assert.Equal("Elegí el producto. Su stock se recalcula a partir de todos sus movimientos.", dialogo.FindControl<TextBlock>("MensajeText")!.Text);
        foreach (var nombre in new[] { "CancelarButton", "RecalcularButton" })
        {
            var b = dialogo.FindControl<Button>(nombre)!;
            var borde = b.TranslatePoint(new Point(b.Bounds.Width, 0), dialogo)!.Value.X;
            Assert.True(borde <= dialogo.Width - 24 + 0.5, $"{nombre} termina en {borde:F1}");
        }
    }

    // ── Deuda D14 (3c-2): sin catalogo.productos el buscador se deshabilita con un aviso en línea ──

    [AvaloniaFact]
    public async Task UnaBusquedaSinPermiso_DeshabilitaElBuscadorYMuestraElAvisoEnLinea()
    {
        var sinPermiso = false;
        var dialogo = new RecalcularStockDialog(
            (_, _) => { sinPermiso = true; return Task.FromResult<IEnumerable<object>>(Array.Empty<object>()); },
            () => sinPermiso);
        Abrir<ProductoDto?>(dialogo);
        var buscador = dialogo.FindControl<AutoCompleteBox>("BuscadorProducto")!;
        var aviso = dialogo.FindControl<Alerta>("AvisoSinPermiso")!;
        Assert.True(buscador.IsEffectivelyEnabled, "Precondición: el buscador arranca habilitado.");
        Assert.False(ArbolVisual.EsVisibleEnArbol(aviso), "Precondición: sin 403 no hay aviso.");

        await buscador.AsyncPopulator!("cem", CancellationToken.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(buscador.IsEffectivelyEnabled);
        Assert.True(ArbolVisual.EsVisibleEnArbol(aviso));
        Assert.Equal("No tenés permiso para buscar productos.", aviso.Texto);
        Assert.Equal(VarianteAlerta.Advertencia, aviso.Variante);
        Assert.False(dialogo.FindControl<Button>("RecalcularButton")!.IsEffectivelyEnabled);
    }

    private sealed class MovimientoServiceFake : IMovimientoStockService
    {
        public Task<MovimientoRegistradoDto> RegistrarAsync(RegistrarMovimientoDto dto, bool forzar = false) => throw new NotSupportedException();
        public Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro)
            => Task.FromResult<IReadOnlyList<MovimientoHistorialDto>>(Array.Empty<MovimientoHistorialDto>());
        public Task<RecalculoResultadoDto> RecalcularStockAsync(int productoId) => throw new NotSupportedException();
    }

    /// <summary>Cuenta las llamadas de búsqueda: cada una que recibe 403 es, en la app real, un Aviso modal global
    /// (AuthTokenHandler dispara ApiSession.AccesoRevocado ante CUALQUIER 403).</summary>
    private sealed class ProductoServiceSin403Contador : IProductoService
    {
        public int Llamadas { get; private set; }
        public Task<int> AltaAsync(StockApp.Domain.Entities.Producto producto) => throw new NotSupportedException();
        public Task ModificarAsync(StockApp.Domain.Entities.Producto producto) => throw new NotSupportedException();
        public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
        public Task CambiarPrecioAsync(int id, decimal precioCosto) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto)
        {
            Llamadas++;
            return Task.FromException<IReadOnlyList<ProductoDto>>(new UnauthorizedAccessException());
        }
    }

    private sealed class DialogoNoUsadoFake : IRecalcularStockDialogService
    {
        public Task<ProductoDto?> ElegirProductoAsync(Func<string?, CancellationToken, Task<IEnumerable<object>>> buscar, Func<bool> sinPermiso)
            => Task.FromResult<ProductoDto?>(null);
    }

    [AvaloniaFact]
    public async Task VariasTeclasSinPermiso_HacenUnaSolaLlamadaYPorLoTantoUnSoloAvisoGlobal()
    {
        var productos = new ProductoServiceSin403Contador();
        var vm = new MovimientoHistorialViewModel(
            new MovimientoServiceFake(), new NavigationServiceFake(), productos, new ConfirmacionServiceFake(),
            new SesionFake(RolUsuario.Operador, Permisos.RegistrarMovimientos, Permisos.RecalcularStock),
            new DialogoNoUsadoFake());
        var dialogo = new RecalcularStockDialog(vm.BuscarProductosAsync, () => vm.BuscadorRecalcularSinPermiso);
        Abrir<ProductoDto?>(dialogo);
        var buscador = dialogo.FindControl<AutoCompleteBox>("BuscadorProducto")!;

        foreach (var texto in new[] { "c", "ce", "cem", "ceme", "cemen", "cement" })
            await buscador.AsyncPopulator!(texto, CancellationToken.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, productos.Llamadas);
        Assert.False(buscador.IsEffectivelyEnabled);
        Assert.True(ArbolVisual.EsVisibleEnArbol(dialogo.FindControl<Alerta>("AvisoSinPermiso")!));
    }
}
