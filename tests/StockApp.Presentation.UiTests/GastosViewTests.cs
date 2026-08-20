using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Authorization;
using StockApp.Application.Auth;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Monta GastosView real (mismo patron que InicioViewTests.cs: VM real + fakes hechos a mano,
/// sin Moq porque este proyecto no lo referencia) para confirmar el bugfix 2026-08-15: los
/// botones "Nuevo gasto" y "Editar" abrian GastoFormView sin ningun gating -- un Operador con
/// solo VerFinanzas (alcanza para entrar a esta pantalla) llegaba a un formulario completo sin
/// boton "Guardar" visible (ese SI esta gateado por PuedeRegistrarGastos en GastoFormView, ver
/// commit a24db43), una puerta a una habitacion sin salida. La propiedad PuedeRegistrarGastos ya
/// existia (usada hoy por el boton "Anular") -- este test cubre que "Nuevo"/"Editar" tambien la
/// usen.
/// </summary>
public class GastosViewTests
{
    private sealed class GastoServiceFake : IGastoService
    {
        public Task<ResultadoGastoDto> AltaAsync(Gasto gasto, IReadOnlyList<int>? movimientoIds = null)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<ResultadoGastoDto> ModificarAsync(Gasto gasto)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task AnularAsync(int id, bool confirmarAnulacionDePagoAutomatico = false)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<Gasto> ObtenerPorIdAsync(int id)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura, string? numeroOrden)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro)
            => Task.FromResult<IReadOnlyList<Gasto>>(Array.Empty<Gasto>());
        public Task<int> RegistrarPagoAsync(PagoGasto pago)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task AnularPagoAsync(int gastoId, int pagoId)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
        public Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    private sealed class CsvExporterFake : ICsvExporter
    {
        public string Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnOrder) => "csv";
    }

    private sealed class ServicioGuardadoArchivoFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarTextoAsync(string contenido, string nombreSugerido) => Task.FromResult(true);
        public Task<bool> GuardarBytesAsync(System.IO.Stream contenido, string nombreSugerido, System.Threading.CancellationToken ct = default) => Task.FromResult(true);
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=GestionMunicipal"
                Width="1100" Height="700">
            <vistas:GastosView />
        </Window>
        """;

    private static (Window Window, GastosViewModel Vm) Montar(RolUsuario rol, IReadOnlySet<string> permisos)
    {
        var vm = new GastosViewModel(
            new GastoServiceFake(),
            new SesionFake(rol, permisos.ToArray()),
            new ProveedorServiceFake(Array.Empty<Proveedor>()),
            new FuenteFinanciamientoServiceFake(Array.Empty<FuenteFinanciamiento>()),
            new RubroGastoServiceFake(Array.Empty<RubroGasto>()),
            new LineaPoaServiceFake(Array.Empty<LineaPoa>()),
            new NavigationServiceFake(),
            new ConfirmacionServiceFake(),
            new CsvExporterFake(),
            new ServicioGuardadoArchivoFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        return (window, vm);
    }

    private static Button BuscarBotonPorTexto(Window window, string texto)
        => window.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == texto);

    [AvaloniaFact]
    public void Montar_OperadorSinRegistrarGastos_OcultaNuevoYEditar()
    {
        var (window, vm) = Montar(RolUsuario.Operador, new HashSet<string> { Permisos.VerFinanzas });

        Assert.False(vm.PuedeRegistrarGastos);
        Assert.False(BuscarBotonPorTexto(window, "Nuevo gasto").IsVisible);
        Assert.False(BuscarBotonPorTexto(window, "Editar").IsVisible);
    }

    [AvaloniaFact]
    public void Montar_OperadorConRegistrarGastos_MuestraNuevoYEditar()
    {
        var (window, vm) = Montar(
            RolUsuario.Operador, new HashSet<string> { Permisos.VerFinanzas, Permisos.RegistrarGastos });

        Assert.True(vm.PuedeRegistrarGastos);
        Assert.True(BuscarBotonPorTexto(window, "Nuevo gasto").IsVisible);
        Assert.True(BuscarBotonPorTexto(window, "Editar").IsVisible);
    }
}
