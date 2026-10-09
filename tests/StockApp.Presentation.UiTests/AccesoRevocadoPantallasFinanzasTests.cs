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
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Hallazgo de la review final de B3 (2026-10-09): el 403 refrescaba el cache de permisos y
/// ShellMainViewModel reenviaba a la pantalla abierta, pero solo Productos sobreescribia el hook
/// virtual; Gastos y el resto de las pantallas con Puede* calculadas sobre ICurrentSession (que no
/// notifica) seguian mostrando los botones viejos. El fix vive en la base
/// (ViewModelBase.NotificarPermisosActualizados notifica todo). Acá se dispara el camino real
/// (ManejadorAccesoRevocado -> shell -> pantalla abierta) con GastosView/GastoFormView reales, SIN
/// navegar. Operador de permisos mixtos: Admin cortocircuita el gate y no probaría nada.
/// </summary>
public class AccesoRevocadoPantallasFinanzasTests
{
    private sealed class GastoServiceFake : IGastoService
    {
        public Task<ResultadoGastoDto> AltaAsync(Gasto gasto, IReadOnlyList<int>? movimientoIds = null) => throw new NotSupportedException();
        public Task<ResultadoGastoDto> ModificarAsync(Gasto gasto) => throw new NotSupportedException();
        public Task AnularAsync(int id, bool confirmarAnulacionDePagoAutomatico = false) => throw new NotSupportedException();
        public Task<Gasto> ObtenerPorIdAsync(int id) => throw new NotSupportedException();
        public Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura, string? numeroOrden) => throw new NotSupportedException();
        public Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro) => Task.FromResult<IReadOnlyList<Gasto>>(Array.Empty<Gasto>());
        public Task<int> RegistrarPagoAsync(PagoGasto pago) => throw new NotSupportedException();
        public Task AnularPagoAsync(int gastoId, int pagoId) => throw new NotSupportedException();
        public Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds) => throw new NotSupportedException();
    }

    private sealed class CsvFake : ICsvExporter
    {
        public string Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnOrder) => "csv";
    }

    private sealed class GuardadoFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarTextoAsync(string contenido, string nombreSugerido) => Task.FromResult(true);
        public Task<bool> GuardarBytesAsync(System.IO.Stream contenido, string nombreSugerido,
            System.Threading.CancellationToken ct = default, string? extension = null, string? tipoMime = null) => Task.FromResult(true);
    }

    private sealed class PdfFake : IPdfExporter
    {
        public byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<ColumnaPdf> columnas, MetadatosDocumento metadatos, ResumenPdf? resumen = null)
            => Array.Empty<byte>();
    }

    private static (Window Ventana, ShellMainViewModel Shell, SesionFake Sesion, HashSet<string> Tras) MontarShell(
        Func<SesionFake, ViewModelBase> pantalla)
    {
        var sesion = new SesionFake(RolUsuario.Operador, new[] { Permisos.VerFinanzas, Permisos.RegistrarGastos });
        var tras = new HashSet<string> { Permisos.VerFinanzas }; // el servidor ya no trae finanzas.gastos
        var shell = new ShellMainViewModel(
            sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        shell.CurrentContent = pantalla(sesion);

        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                    Width="1280" Height="760"><vistas:ShellMainView /></Window>
            """, typeof(TestApp).Assembly);
        ventana.DataTemplates.Add(new ViewLocator());
        ventana.DataContext = shell;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return (ventana, shell, sesion, tras);
    }

    private static Button Boton(Window ventana, string texto)
        => ventana.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == texto);

    private static async Task Disparar403(Window ventana, ShellMainViewModel shell, SesionFake sesion, HashSet<string> tras)
    {
        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras),
            new ConfirmacionServiceFake(), shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Un403_ConGastosAbierto_OcultaLosBotonesDeEscrituraSinNavegar()
    {
        var (ventana, shell, sesion, tras) = MontarShell(s => new GastosViewModel(
            new GastoServiceFake(), s,
            new ProveedorServiceFake(Array.Empty<Proveedor>()),
            new FuenteFinanciamientoServiceFake(Array.Empty<FuenteFinanciamiento>()),
            new RubroGastoServiceFake(Array.Empty<RubroGasto>()),
            new LineaPoaServiceFake(Array.Empty<LineaPoa>()),
            new NavigationServiceFake(), new ConfirmacionServiceFake(),
            new CsvFake(), new GuardadoFake(), new PdfFake(), new ServicioAperturaArchivoFake()));

        Assert.True(Boton(ventana, "Nuevo gasto").IsVisible, "Precondición: con finanzas.gastos, Nuevo se ve.");
        Assert.True(Boton(ventana, "Anular").IsVisible);

        await Disparar403(ventana, shell, sesion, tras);

        Assert.False(ArbolVisual.EsVisibleEnArbol(Boton(ventana, "Nuevo gasto")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(Boton(ventana, "Editar")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(Boton(ventana, "Anular")));
    }

    [AvaloniaFact]
    public async Task Un403_ConElFormularioDeGastoAbierto_OcultaGuardarSinBorrarLoTipeado()
    {
        GastoFormViewModel? form = null;
        var (ventana, shell, sesion, tras) = MontarShell(s =>
        {
            var adjuntos = new AdjuntosPanelViewModel(
                new AdjuntoServiceFake(), new ServicioSeleccionArchivoFake(),
                new ServicioAperturaArchivoFake(), new ConfirmacionServiceFake(), s);
            form = new GastoFormViewModel(
                new GastoServiceFake(), s,
                new ProveedorServiceFake(Array.Empty<Proveedor>()),
                new FuenteFinanciamientoServiceFake(Array.Empty<FuenteFinanciamiento>()),
                new RubroGastoServiceFake(Array.Empty<RubroGasto>()),
                new LineaPoaServiceFake(Array.Empty<LineaPoa>()),
                new NavigationServiceFake(), new ConfirmacionServiceFake(), adjuntos);
            return form;
        });
        form!.MontoTexto = "1.500,50";
        form.Detalle = "Materiales de obra";
        form.NumeroFactura = "A-0001234";
        Dispatcher.UIThread.RunJobs();

        Assert.True(Boton(ventana, "Guardar").IsVisible, "Precondición: con finanzas.gastos, Guardar se ve.");

        await Disparar403(ventana, shell, sesion, tras);

        Assert.False(ArbolVisual.EsVisibleEnArbol(Boton(ventana, "Guardar")));
        Assert.Equal("1.500,50", form.MontoTexto);
        Assert.Equal("Materiales de obra", form.Detalle);
        Assert.Equal("A-0001234", form.NumeroFactura);
        var textos = ventana.GetVisualDescendants().OfType<TextBox>().Select(t => t.Text).ToList();
        Assert.Contains("1.500,50", textos);
        Assert.Contains("Materiales de obra", textos);
        Assert.Contains("A-0001234", textos);
    }
}
