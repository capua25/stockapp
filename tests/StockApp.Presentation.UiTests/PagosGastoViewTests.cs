using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    private sealed class CurrentSessionFake : ICurrentSession
    {
        private readonly RolUsuario _rol;
        private readonly IReadOnlySet<string> _permisos;

        public CurrentSessionFake(RolUsuario rol, IReadOnlySet<string> permisos)
        {
            _rol = rol;
            _permisos = permisos;
        }

        public bool EstaAutenticado => true;
        public UsuarioSesion? UsuarioActual => new(1, "operador", _rol, "Operador de prueba");
        public RolUsuario? RolActual => _rol;
        public IReadOnlySet<string> PermisosActuales => _permisos;
        public void EstablecerPermisos(IReadOnlySet<string> permisos) { }
        public void IniciarSesion(Usuario usuario) => throw new NotSupportedException("No usado en este banco de pruebas.");
        public void CerrarSesion() => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

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
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=StockApp.Presentation"
                Width="900" Height="700">
            <vistas:PagosGastoView />
        </Window>
        """;

    private static (Window Window, PagosGastoViewModel Vm) Montar(RolUsuario rol, IReadOnlySet<string> permisos)
    {
        var session = new CurrentSessionFake(rol, permisos);
        var adjuntosPanel = new AdjuntosPanelViewModel(
            new AdjuntoServiceFake(),
            new ServicioSeleccionArchivoFake(),
            new ServicioAperturaArchivoFake(),
            new ConfirmacionServiceFake(),
            session);

        var vm = new PagosGastoViewModel(
            new GastoServiceFake(CrearGasto()),
            session,
            new NavigationServiceFake(),
            new ConfirmacionServiceFake(),
            adjuntosPanel);
        vm.CargarParaGasto(CrearGasto());

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
}
