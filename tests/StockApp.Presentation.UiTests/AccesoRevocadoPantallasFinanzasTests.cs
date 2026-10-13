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
        public Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro) => Task.FromResult<IReadOnlyList<Gasto>>(
            new[] { new Gasto { Id = 1, Detalle = "Factura vieja", Fecha = DateTime.UtcNow, MontoTotal = 1000m } });
        public Task<int> RegistrarPagoAsync(PagoGasto pago) => throw new NotSupportedException();
        public Task AnularPagoAsync(int gastoId, int pagoId) => throw new NotSupportedException();
        public Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds) => throw new NotSupportedException();
    }

    private sealed class GuardadoFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarBytesAsync(System.IO.Stream contenido, string nombreSugerido,
            System.Threading.CancellationToken ct = default, string? extension = null, string? tipoMime = null) => Task.FromResult(true);
    }

    private sealed class PdfFake : IPdfExporter
    {
        public byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<ColumnaPdf> columnas, MetadatosDocumento metadatos, ResumenPdf? resumen = null)
            => Array.Empty<byte>();
    }

    private static (Window Ventana, ShellMainViewModel Shell, SesionFake Sesion, HashSet<string> Tras) MontarShell(
        Func<SesionFake, ViewModelBase> pantalla,
        string[]? permisosAntes = null, string[]? permisosTras = null)
    {
        var sesion = new SesionFake(RolUsuario.Operador, permisosAntes ?? new[] { Permisos.VerFinanzas, Permisos.RegistrarGastos });
        // por defecto el servidor ya no trae finanzas.gastos
        var tras = new HashSet<string>(permisosTras ?? new[] { Permisos.VerFinanzas });
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
            new GuardadoFake(), new PdfFake(), new ServicioAperturaArchivoFake()));

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

    /// <summary>
    /// El panel de adjuntos es un VM hijo del formulario. El 403 llega al formulario (pantalla abierta) y el formulario
    /// tiene que reenviarlo al panel: su OnPropertyChanged(string.Empty) relee AdjuntosPanel, que es la misma instancia,
    /// y los bindings del panel no se reevalúan. "Disponible" = visible y habilitado: el panel viejo deshabilitaba y el v2
    /// oculta. Operador de permisos mixtos: Admin cortocircuita el gate.
    /// </summary>
    [AvaloniaFact]
    public async Task Un403_ConLaFacturaEnEdicion_AgregarYQuitarAdjuntoDejanDeEstarDisponibles()
    {
        var (ventana, shell, sesion, tras) = MontarShell(FormularioEnEdicionConUnAdjunto);

        Assert.True(Disponible(BotonDelPanel(ventana, "Agregar")), "Precondición: con finanzas.gastos, Agregar adjunto está disponible.");
        Assert.True(Disponible(BotonDelPanel(ventana, "Quitar")), "Precondición: con finanzas.gastos, Quitar adjunto está disponible.");

        await Disparar403(ventana, shell, sesion, tras);

        Assert.False(Disponible(BotonDelPanel(ventana, "Agregar")));
        Assert.False(Disponible(BotonDelPanel(ventana, "Quitar")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonDelPanel(ventana, "Agregar")), "El panel v2 oculta (no deshabilita) sin permiso.");
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonDelPanel(ventana, "Quitar")));
    }

    [AvaloniaFact]
    public async Task Un403_QueQuitaPagos_NoTocaElPanelDeAdjuntosDeUnGasto()
    {
        var (ventana, shell, sesion, tras) = MontarShell(FormularioEnEdicionConUnAdjunto,
            permisosAntes: new[] { Permisos.VerFinanzas, Permisos.RegistrarGastos, Permisos.RegistrarPagos },
            permisosTras: new[] { Permisos.VerFinanzas, Permisos.RegistrarGastos });

        await Disparar403(ventana, shell, sesion, tras);

        Assert.True(Disponible(BotonDelPanel(ventana, "Agregar")), "El panel de un gasto depende de finanzas.gastos, no de finanzas.pagos.");
        Assert.True(Disponible(BotonDelPanel(ventana, "Quitar")));
    }

    private static GastoFormViewModel FormularioEnEdicionConUnAdjunto(SesionFake s)
    {
        var servicioAdjuntos = new AdjuntoServiceFake();
        servicioAdjuntos.Items.Add(new AdjuntoDto(1, "factura-A-0001234.pdf", "application/pdf", 182_400, 42, null,
            new DateTime(2026, 10, 6, 15, 20, 0, DateTimeKind.Utc)));
        var adjuntos = new AdjuntosPanelViewModel(
            servicioAdjuntos, new ServicioSeleccionArchivoFake(),
            new ServicioAperturaArchivoFake(), new ConfirmacionServiceFake(), s);
        var form = new GastoFormViewModel(
            new GastoServiceFake(), s,
            new ProveedorServiceFake(Array.Empty<Proveedor>()),
            new FuenteFinanciamientoServiceFake(Array.Empty<FuenteFinanciamiento>()),
            new RubroGastoServiceFake(Array.Empty<RubroGasto>()),
            new LineaPoaServiceFake(Array.Empty<LineaPoa>()),
            new NavigationServiceFake(), new ConfirmacionServiceFake(), adjuntos);
        form.CargarParaEditar(new Gasto
        {
            Id = 42, ProveedorId = 1, Detalle = "Materiales de obra", Fecha = DateTime.UtcNow,
            MontoTotal = 100m, FuenteFinanciamientoId = 1, RubroGastoId = 1,
        });
        return form;
    }

    private static Button BotonDelPanel(Window ventana, string texto) =>
        ventana.GetVisualDescendants().OfType<StockApp.Presentation.Views.Finanzas.AdjuntosPanelView>().Single()
            .GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == texto);

    private static bool Disponible(Button boton) => boton.IsEffectivelyEnabled && ArbolVisual.EsVisibleEnArbol(boton);

    private static GastosViewModel NuevoGastosVm(SesionFake s) => new(
        new GastoServiceFake(), s,
        new ProveedorServiceFake(Array.Empty<Proveedor>()),
        new FuenteFinanciamientoServiceFake(Array.Empty<FuenteFinanciamiento>()),
        new RubroGastoServiceFake(Array.Empty<RubroGasto>()),
        new LineaPoaServiceFake(Array.Empty<LineaPoa>()),
        new NavigationServiceFake(), new ConfirmacionServiceFake(),
        new GuardadoFake(), new PdfFake(), new ServicioAperturaArchivoFake());

    private static Button BotonConComando(Window ventana, System.Windows.Input.ICommand comando)
        => ventana.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, comando));

    /// <summary>
    /// Deuda 3c-2 (decisión 2026-10-10): Exportar PDF serializa en el cliente las filas ya
    /// cargadas; tras un 403 que quita finanzas.ver el botón se oculta y la grilla se vacía.
    /// Operador de permisos mixtos (Admin cortocircuita el gate). Se conserva RegistrarGastos para
    /// probar que el gate es específico: "Nuevo gasto" sigue visible.
    /// </summary>
    [AvaloniaFact]
    public async Task Un403_QueQuitaVerFinanzas_OcultaExportarYVaciaLaGrilla()
    {
        GastosViewModel? vm = null;
        var (ventana, shell, sesion, tras) = MontarShell(
            s => vm = NuevoGastosVm(s),
            permisosAntes: new[] { Permisos.VerFinanzas, Permisos.RegistrarGastos },
            permisosTras: new[] { Permisos.RegistrarGastos });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, vm!.Filas.Count);
        Assert.True(ArbolVisual.EsVisibleEnArbol(BotonConComando(ventana, vm.ExportarPdfCommand)), "Precondición: con finanzas.ver, Exportar PDF se ve.");

        await Disparar403(ventana, shell, sesion, tras);

        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonConComando(ventana, vm.ExportarPdfCommand)));
        Assert.Empty(vm.Filas);
        Assert.True(ArbolVisual.EsVisibleEnArbol(Boton(ventana, "Nuevo gasto")), "El gate es específico: RegistrarGastos sigue.");
    }

    [AvaloniaFact]
    public void SinVerFinanzas_DesdeElMontaje_ExportarNoSeVe()
    {
        GastosViewModel? vm = null;
        var (ventana, _, _, _) = MontarShell(
            s => vm = NuevoGastosVm(s),
            permisosAntes: new[] { Permisos.RegistrarGastos });

        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonConComando(ventana, vm!.ExportarPdfCommand)));
    }
}
