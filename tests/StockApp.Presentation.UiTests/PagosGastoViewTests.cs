using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Optris.Icons.Avalonia;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Monta PagosGastoView real (mismo patrón que IngresosViewTests.cs) para confirmar el bugfix
/// 2026-08-16: el botón "Registrar pago" ya estaba gateado por PuedeRegistrarPagos (bugfix
/// 2026-08-15), pero el FORMULARIO (fecha/monto/nota) se seguía mostrando completo — un Operador
/// sin RegistrarPagos podía llenarlo entero y no tenía cómo guardarlo, el mismo callejón sin
/// salida ya cerrado en los formularios de Gasto/Ingreso. Reutiliza PuedeRegistrarPagos (ya
/// existía) para ocultar el panel de campos entero, no solo el botón. "Volver" queda siempre
/// visible (a diferencia de IngresosView, esta pantalla SIEMPRE tiene un botón de acción para el
/// usuario de solo lectura) y se agrega el indicador "Solo lectura" (patrón IngresosView.axaml,
/// commit 2243e23) para que la pantalla no se lea como rota.
/// </summary>
public class PagosGastoViewTests
{
    private sealed class GastoServiceFake : IGastoService
    {
        private readonly Gasto _gasto;

        public GastoServiceFake(Gasto gasto) => _gasto = gasto;

        public Task<ResultadoGastoDto> AltaAsync(Gasto gasto, IReadOnlyList<int>? movimientoIds = null)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<ResultadoGastoDto> ModificarAsync(Gasto gasto)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task AnularAsync(int id, bool confirmarAnulacionDePagoAutomatico = false)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<Gasto> ObtenerPorIdAsync(int id) => Task.FromResult(_gasto);
        public Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura, string? numeroOrden)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<int> RegistrarPagoAsync(PagoGasto pago)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task AnularPagoAsync(int gastoId, int pagoId)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    private static Gasto CrearGasto() => new()
    {
        Id = 1,
        ProveedorId = 1,
        Proveedor = new Proveedor { Id = 1, Nombre = "Proveedor de prueba" },
        NumeroFactura = "A-1",
        Detalle = "Materiales",
        MontoTotal = 1000m,
        Pagos = new List<PagoGasto>(),
    };

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=GestionMunicipal"
                Width="900" Height="700">
            <vistas:PagosGastoView />
        </Window>
        """;

    private static (Window Window, PagosGastoViewModel Vm) Montar(
        RolUsuario rol, IReadOnlySet<string> permisos, Gasto? gasto = null)
    {
        gasto ??= CrearGasto();
        var session = new SesionFake(rol, permisos.ToArray());
        var adjuntosPanel = new AdjuntosPanelViewModel(
            new AdjuntoServiceFake(),
            new ServicioSeleccionArchivoFake(),
            new ServicioAperturaArchivoFake(),
            new ConfirmacionServiceFake(),
            session);

        var vm = new PagosGastoViewModel(
            new GastoServiceFake(gasto),
            session,
            new NavigationServiceFake(),
            new ConfirmacionServiceFake(),
            adjuntosPanel);
        vm.CargarParaGasto(gasto);

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await InicializarAsync() del DataContextChanged

        return (window, vm);
    }

    private static Button BuscarBotonPorTexto(Window window, string texto)
        => window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == texto);

    private static TextBlock BuscarTextoPorContenido(Window window, string texto)
        => window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == texto);

    private static Control BuscarPanelFormularioPago(Window window)
        => window.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PanelRegistrarPago");

    [AvaloniaFact]
    public void Montar_OperadorSinRegistrarPagos_OcultaFormularioCompleto()
    {
        var (window, vm) = Montar(RolUsuario.Operador, new HashSet<string> { Permisos.VerFinanzas });

        Assert.False(vm.PuedeRegistrarPagos);
        Assert.False(BuscarPanelFormularioPago(window).IsVisible);
        Assert.False(BuscarBotonPorTexto(window, "Registrar pago").IsVisible);
    }

    [AvaloniaFact]
    public void Montar_OperadorConRegistrarPagos_MuestraFormularioCompleto()
    {
        var (window, vm) = Montar(
            RolUsuario.Operador, new HashSet<string> { Permisos.VerFinanzas, Permisos.RegistrarPagos });

        Assert.True(vm.PuedeRegistrarPagos);
        Assert.True(BuscarPanelFormularioPago(window).IsVisible);
        Assert.True(BuscarBotonPorTexto(window, "Registrar pago").IsVisible);
    }

    [AvaloniaFact]
    public void Montar_OperadorSinRegistrarPagos_VolverSigueVisible()
    {
        var (window, _) = Montar(RolUsuario.Operador, new HashSet<string> { Permisos.VerFinanzas });

        Assert.True(BuscarBotonPorTexto(window, "Volver").IsVisible);
    }

    // ── Indicador "Solo lectura" (mismo patrón que IngresosView.axaml, commit 2243e23) ────────

    [AvaloniaFact]
    public void Montar_OperadorSinRegistrarPagos_MuestraIndicadorSoloLectura()
    {
        var (window, vm) = Montar(RolUsuario.Operador, new HashSet<string> { Permisos.VerFinanzas });

        Assert.False(vm.PuedeRegistrarPagos);
        Assert.True(BuscarTextoPorContenido(window, "Solo lectura").IsVisible);
    }

    [AvaloniaFact]
    public void Montar_OperadorConRegistrarPagos_OcultaIndicadorSoloLectura()
    {
        var (window, vm) = Montar(
            RolUsuario.Operador, new HashSet<string> { Permisos.VerFinanzas, Permisos.RegistrarPagos });

        Assert.True(vm.PuedeRegistrarPagos);
        Assert.False(BuscarTextoPorContenido(window, "Solo lectura").IsVisible);
    }

    [AvaloniaFact]
    public void Montar_Admin_OcultaIndicadorSoloLectura()
    {
        var (window, vm) = Montar(RolUsuario.Admin, new HashSet<string>());

        Assert.True(vm.PuedeRegistrarPagos);
        Assert.False(BuscarTextoPorContenido(window, "Solo lectura").IsVisible);
    }

    // ── Migración de ListBox a DataGrid (2026-08-24) ────────────────────────────────────────

    private static Gasto CrearGastoConPago(PagoGasto pago)
    {
        var gasto = CrearGasto();
        gasto.Pagos.Add(pago);
        return gasto;
    }

    /// <summary>
    /// Repro del bugfix "botón sin ícono visible en celda de DataGrid" (ver comentario XAML de
    /// <c>DataGridCell.sin-padding-vertical</c>, IngresoPorFacturaView.axaml/
    /// NuevaImportacionView.axaml), aplicado acá al botón "Anular". Medido con un XAML mínimo
    /// (Button.secondary + i:Icon "mdi-cancel" dentro de una DataGridTemplateColumn): el control
    /// <c>Optris.Icons.Avalonia.Icon</c> mide 2px de alto SIN el fix (RowHeight 36 - PaddingCelda
    /// vertical 16 = 20px útiles, insuficientes) y 13px CON el fix
    /// (CellStyleClasses="sin-padding-vertical" + el Style en UserControl.Styles). Se mide el
    /// <c>Icon</c> en sí, no el <c>Image</c> interno: el <c>Image</c> tiene alto intrínseco fijo
    /// (13px) en ambos casos y solo cambia su Y -- no delata el aplastamiento.
    /// </summary>
    [AvaloniaFact]
    public void Montar_PagoActivoConPermiso_IconoAnular_TieneAltoRenderizadoVisible_DentroDeLaCeldaDelDataGrid()
    {
        var pago = new PagoGasto { Id = 1, GastoId = 1, Fecha = DateTime.Today, Monto = 500m, Activo = true };
        var (window, _) = Montar(RolUsuario.Admin, new HashSet<string>(), CrearGastoConPago(pago));

        var botonAnular = window.GetVisualDescendants().OfType<Button>()
            .Single(b => ReferenceEquals(b.DataContext, pago));
        var icono = botonAnular.GetVisualDescendants().OfType<Icon>().Single();

        Assert.True(icono.Bounds.Height > 10,
            $"El ícono del botón 'Anular' mide {icono.Bounds.Height}px de alto dentro de la " +
            "celda del DataGrid -- aplastado si la celda conserva el padding vertical de 8px " +
            "(RowHeight 36 - 16 = 20px útiles, insuficientes). Verificar que la " +
            "DataGridTemplateColumn tenga CellStyleClasses=\"sin-padding-vertical\" en " +
            "PagosGastoView.axaml.");

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Montar_PagoInactivo_OcultaBotonAnular_YMuestraBadgeAnulado()
    {
        var pago = new PagoGasto { Id = 2, GastoId = 1, Fecha = DateTime.Today, Monto = 300m, Activo = false };
        var (window, _) = Montar(RolUsuario.Admin, new HashSet<string>(), CrearGastoConPago(pago));

        var botonAnular = window.GetVisualDescendants().OfType<Button>()
            .Single(b => ReferenceEquals(b.DataContext, pago));
        Assert.False(botonAnular.IsVisible);

        var badge = window.GetVisualDescendants()
            .OfType<StockApp.Presentation.Controls.BadgeEstado>()
            .Single(b => ReferenceEquals(b.DataContext, pago));
        Assert.True(badge.IsVisible);
        Assert.Equal("Anulado", badge.Texto);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Montar_OperadorSinRegistrarPagos_OcultaBotonAnular_AunqueElPagoEsteActivo()
    {
        var pago = new PagoGasto { Id = 3, GastoId = 1, Fecha = DateTime.Today, Monto = 300m, Activo = true };
        var (window, _) = Montar(
            RolUsuario.Operador, new HashSet<string> { Permisos.VerFinanzas }, CrearGastoConPago(pago));

        var botonAnular = window.GetVisualDescendants().OfType<Button>()
            .Single(b => ReferenceEquals(b.DataContext, pago));
        Assert.False(botonAnular.IsVisible);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Montar_SeleccionarFilaEnGrilla_ActualizaPagoSeleccionadoEnElViewModel()
    {
        var pago = new PagoGasto { Id = 4, GastoId = 1, Fecha = DateTime.Today, Monto = 300m, Activo = true };
        var (window, vm) = Montar(RolUsuario.Admin, new HashSet<string>(), CrearGastoConPago(pago));

        var grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
        grid.SelectedItem = pago;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(pago, vm.PagoSeleccionado);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
