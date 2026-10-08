using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.Views.Movimientos;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Registrar Entrada / Salida v2 (spec §5.2) con las Views REALES (los wrappers), clicks reales y el tema real.</summary>
public class MovimientoFormControlV2Tests
{
    /// <summary>Registra cada llamada; con StockActualAlFaltar, el primer intento sin forzar responde stock insuficiente.</summary>
    private sealed class MovimientosGuionado : IMovimientoStockService
    {
        public decimal? StockActualAlFaltar { get; set; }
        public Exception? Falla { get; set; }
        public List<(RegistrarMovimientoDto Dto, bool Forzar)> Llamadas { get; } = new();

        public Task<MovimientoRegistradoDto> RegistrarAsync(RegistrarMovimientoDto dto, bool forzar = false)
        {
            Llamadas.Add((dto, forzar));
            if (Falla is not null) throw Falla;
            if (StockActualAlFaltar is { } stock && !forzar)
                throw new StockInsuficienteException(dto.ProductoId, stock, dto.Cantidad);
            return Task.FromResult(new MovimientoRegistradoDto(50, dto.ProductoId, dto.Tipo, dto.Motivo, dto.Cantidad,
                dto.PrecioUnitario ?? 0m, 10m, 10m + dto.Cantidad, new DateTime(2026, 10, 7, 13, 0, 0, DateTimeKind.Utc)));
        }

        public Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro) => throw new NotSupportedException();
        public Task<RecalculoResultadoDto> RecalcularStockAsync(int productoId) => throw new NotSupportedException();
    }

    /// <summary>Responde en orden lo guionado y guarda cada pregunta; una pregunta no guionada falla.</summary>
    private sealed class ConfirmacionGuionada : IConfirmacionService
    {
        private readonly Queue<bool> _respuestas;
        public ConfirmacionGuionada(params bool[] respuestas) => _respuestas = new Queue<bool>(respuestas);
        public List<string> Preguntas { get; } = new();

        public Task<bool> PreguntarAsync(string mensaje)
        {
            Preguntas.Add(mensaje);
            return Task.FromResult(_respuestas.Dequeue());
        }

        public Task InformarAsync(string mensaje) => Task.CompletedTask;
        public Task<string?> PedirTextoAsync(string titulo, string mensaje) => throw new NotSupportedException();
    }

    private static ProductoDto P(int id, string nombre, bool activo = true) => new(id, $"C-{id:000}", null, nombre, null,
        null, null, null, 1, "Unidad", 100m, 10m, 0m, activo, new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc));

    private static readonly ProductoDto[] Catalogo = [P(1, "Cemento portland 50 kg"), P(7, "Cable 2x1,5 mm"), P(12, "Carretilla reforzada", activo: false)];

    private sealed record Montaje(Window Ventana, UserControl Vista, MovimientoRegistroViewModelBase Vm,
        MovimientosGuionado Servicio, NavigationRecorderFake Navegacion, ConfirmacionGuionada Confirmacion);

    private static Montaje Montar(bool salida = false, ConfirmacionGuionada? confirmacion = null)
    {
        var servicio = new MovimientosGuionado();
        var navegacion = new NavigationRecorderFake();
        var conf = confirmacion ?? new ConfirmacionGuionada();
        var productos = new ProductoServiceIngresoFake(Catalogo);
        MovimientoRegistroViewModelBase vm;
        UserControl vista;
        if (salida)
        {
            var s = new SalidaRegistroViewModel(servicio, productos, navegacion, conf);
            vm = s;
            vista = new SalidaRegistroView { DataContext = s };
        }
        else
        {
            var e = new EntradaRegistroViewModel(servicio, productos, navegacion, conf);
            vm = e;
            vista = new EntradaRegistroView { DataContext = e };
        }

        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await InicializarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio, navegacion, conf);
    }

    private static void Completar(Montaje m, string producto, decimal cantidad, MotivoMovimiento motivo, decimal? precio = null)
    {
        m.Vm.Motivo = motivo;
        m.Vm.ProductoSeleccionado = m.Vm.Productos.Single(p => p.Nombre == producto);
        m.Vm.Cantidad = cantidad;
        m.Vm.PrecioUnitario = precio;
        Dispatcher.UIThread.RunJobs();
    }

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Rect EnVentana(Window ventana, Control c) =>
        new(c.TranslatePoint(new Point(0, 0), ventana)!.Value, c.Bounds.Size);

    private static Rect Campo(Montaje m, string nombre) => EnVentana(m.Ventana, MontajeV2.Parte<CampoFormulario>(m.Vista, nombre));

    [AvaloniaTheory]
    [InlineData(false, "Registrar Entrada")]
    [InlineData(true, "Registrar Salida")]
    public void Raiz_TieneUi2_YEncabezadoSinBreadcrumbConElTituloDelVm(bool salida, string titulo)
    {
        var m = Montar(salida);
        var control = m.Vista.GetVisualDescendants().OfType<MovimientoFormControl>().Single();
        var encabezado = MontajeV2.Parte<EncabezadoPagina>(m.Vista, "Encabezado");

        Assert.Contains("ui2", control.Classes);
        Assert.DoesNotContain("ui2", m.Vista.Classes); // el wrapper no cambia
        Assert.Equal(titulo, encabezado.Titulo);
        Assert.Null(encabezado.BreadcrumbOrigen);
        Assert.False(ArbolVisual.EsVisibleEnArbol(encabezado.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_Breadcrumb")));
    }

    [AvaloniaFact]
    public void Tarjeta_Mide760YQuedaALaIzquierda()
    {
        var m = Montar();
        var tarjeta = EnVentana(m.Ventana, MontajeV2.Parte<TarjetaContenido>(m.Vista, "Tarjeta"));

        Assert.Equal(760, tarjeta.Width);
        Assert.Equal(28, tarjeta.X);
    }

    [AvaloniaFact]
    public void Campos_ProductoYComentarioACompleto_CantidadYMotivoEnDosColumnas()
    {
        var m = Montar();

        Assert.Equal(Campo(m, "CampoProducto").X, Campo(m, "CampoCantidad").X);
        Assert.Equal(Campo(m, "CampoProducto").Right, Campo(m, "CampoMotivo").Right);
        Assert.Equal(Campo(m, "CampoCantidad").Y, Campo(m, "CampoMotivo").Y);
        Assert.Equal(16, Campo(m, "CampoMotivo").X - Campo(m, "CampoCantidad").Right);
        Assert.Equal(Campo(m, "CampoCantidad").X, Campo(m, "CampoPrecio").X);
        Assert.Equal(Campo(m, "CampoCantidad").Width, Campo(m, "CampoPrecio").Width);
        Assert.Equal(Campo(m, "CampoProducto").Width, Campo(m, "CampoComentario").Width);
        Assert.Equal(88, MontajeV2.Parte<CampoFormulario>(m.Vista, "CampoComentario").GetVisualDescendants().OfType<TextBox>().First().Bounds.Height);
    }

    [AvaloniaFact]
    public void ConPrecio_LasFilasVanA16()
    {
        var m = Montar(); // Entrada arranca en Compra: el precio se ve
        var filaCantidad = Math.Max(Campo(m, "CampoCantidad").Bottom, Campo(m, "CampoMotivo").Bottom);

        Assert.Equal(16, Campo(m, "CampoCantidad").Y - Campo(m, "CampoProducto").Bottom);
        Assert.Equal(16, Campo(m, "CampoPrecio").Y - filaCantidad);
        Assert.Equal(16, Campo(m, "CampoComentario").Y - Campo(m, "CampoPrecio").Bottom);
    }

    [AvaloniaTheory]
    [InlineData(MotivoMovimiento.UsoOConsumo)]
    [InlineData(MotivoMovimiento.Merma)]
    [InlineData(MotivoMovimiento.Ajuste)]
    public void SinFilaDePrecio_ElComentarioQuedaA16DeLaFilaDeCantidad(MotivoMovimiento motivo)
    {
        var m = Montar(salida: true);
        m.Vm.Motivo = motivo;
        Dispatcher.UIThread.RunJobs();
        var filaCantidad = Math.Max(Campo(m, "CampoCantidad").Bottom, Campo(m, "CampoMotivo").Bottom);

        Assert.False(ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<CampoFormulario>(m.Vista, "CampoPrecio")));
        Assert.Equal(16, Campo(m, "CampoComentario").Y - filaCantidad);
    }

    [AvaloniaFact]
    public void Requeridos_SegunTipoYMotivo()
    {
        static List<string?> Requeridos(Montaje m) => m.Vista.GetVisualDescendants().OfType<CampoFormulario>()
            .Where(c => c.Requerido && ArbolVisual.EsVisibleEnArbol(c)).Select(c => c.Etiqueta).ToList();

        var entrada = Montar();
        Assert.Equal(new string?[] { "Producto", "Cantidad", "Precio unitario" }, Requeridos(entrada));

        entrada.Vm.Motivo = MotivoMovimiento.Ajuste;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new string?[] { "Producto", "Cantidad" }, Requeridos(entrada));

        Assert.Equal(new string?[] { "Producto", "Cantidad" }, Requeridos(Montar(salida: true)));
    }

    [AvaloniaFact]
    public void Pie_RegistrarEsElUnicoPrimarioYQuedaALaDerecha()
    {
        var m = Montar();
        var registrar = MontajeV2.Parte<Button>(m.Vista, "BotonRegistrar");
        var tarjeta = EnVentana(m.Ventana, MontajeV2.Parte<TarjetaContenido>(m.Vista, "Tarjeta"));

        Assert.Equal("Registrar", registrar.Content);
        Assert.Same(m.Vm.RegistrarCommand, registrar.Command);
        Assert.NotEqual(HorizontalAlignment.Stretch, registrar.HorizontalAlignment);
        Assert.Equal(tarjeta.Right - 1 - 24, EnVentana(m.Ventana, registrar).Right, 0);
        Assert.Single(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primary"));
    }

    [AvaloniaFact]
    public void Registrar_SinProducto_Deshabilitado_YConDatosValidos_Habilitado()
    {
        var m = Montar(salida: true);
        var registrar = MontajeV2.Parte<Button>(m.Vista, "BotonRegistrar");
        Assert.False(registrar.IsEffectivelyEnabled);

        Completar(m, "Cable 2x1,5 mm", 3m, MotivoMovimiento.UsoOConsumo);

        Assert.True(registrar.IsEffectivelyEnabled);
        Assert.DoesNotContain(m.Vm.Productos, p => p.Nombre == "Carretilla reforzada"); // inactivo: el VM lo filtra
    }

    [AvaloniaFact]
    public void Salida_StockInsuficiente_SiNoConfirma_NoRegistraNiNavega()
    {
        var m = Montar(salida: true, confirmacion: new ConfirmacionGuionada(false));
        m.Servicio.StockActualAlFaltar = 2m;
        Completar(m, "Cable 2x1,5 mm", 5m, MotivoMovimiento.UsoOConsumo);

        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonRegistrar"));

        Assert.Equal("El stock quedará en -3. ¿Confirmar la salida igual?", Assert.Single(m.Confirmacion.Preguntas));
        Assert.False(Assert.Single(m.Servicio.Llamadas).Forzar);
        Assert.Null(m.Navegacion.UltimoTipoNavegado);
        Assert.False(ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<Alerta>(m.Vista, "AlertaError")));
    }

    [AvaloniaFact]
    public void Salida_StockInsuficiente_SiConfirma_RegistraForzandoYVaAlHistorial()
    {
        var m = Montar(salida: true, confirmacion: new ConfirmacionGuionada(true));
        m.Servicio.StockActualAlFaltar = 2m;
        Completar(m, "Cable 2x1,5 mm", 5m, MotivoMovimiento.UsoOConsumo);

        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonRegistrar"));

        Assert.Equal(new[] { false, true }, m.Servicio.Llamadas.Select(l => l.Forzar));
        Assert.Null(m.Servicio.Llamadas[1].Dto.PrecioUnitario);
        Assert.Equal(typeof(MovimientoHistorialViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void Entrada_Compra_OfreceAsociarFactura_YSiAceptaVaAlGasto()
    {
        var m = Montar(confirmacion: new ConfirmacionGuionada(true));
        Completar(m, "Cemento portland 50 kg", 40m, MotivoMovimiento.Compra, precio: 480m);

        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonRegistrar"));

        Assert.Equal("Entrada registrada. ¿Desea asociar una factura de proveedor a esta entrada?", Assert.Single(m.Confirmacion.Preguntas));
        Assert.Equal(480m, Assert.Single(m.Servicio.Llamadas).Dto.PrecioUnitario);
        Assert.Equal(typeof(GastoFormViewModel), m.Navegacion.UltimoTipoNavegado);
        Assert.IsType<Action<GastoFormViewModel>>(m.Navegacion.UltimoInicializador);
    }

    [AvaloniaFact]
    public void Entrada_Ajuste_NoPreguntaPorFactura_YVaAlHistorial()
    {
        var m = Montar();
        Completar(m, "Cemento portland 50 kg", 2m, MotivoMovimiento.Ajuste);

        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonRegistrar"));

        Assert.Empty(m.Confirmacion.Preguntas);
        Assert.Single(m.Servicio.Llamadas);
        Assert.Equal(typeof(MovimientoHistorialViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void Registrar_ConErrorDelServicio_MuestraLaAlertaYNoNavega()
    {
        var m = Montar();
        m.Servicio.Falla = new ReglaDeNegocioException("No se permiten movimientos sobre productos inactivos (ProductoId=1).");
        Completar(m, "Cemento portland 50 kg", 2m, MotivoMovimiento.Ajuste);
        Assert.False(ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<Alerta>(m.Vista, "AlertaError")));

        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonRegistrar"));

        var alerta = MontajeV2.Parte<Alerta>(m.Vista, "AlertaError");
        Assert.True(ArbolVisual.EsVisibleEnArbol(alerta));
        Assert.Equal(VarianteAlerta.Error, alerta.Variante);
        Assert.Equal("No se permiten movimientos sobre productos inactivos (ProductoId=1).", alerta.Texto);
        Assert.Null(m.Navegacion.UltimoTipoNavegado);
        Assert.Equal("Cemento portland 50 kg", m.Vm.ProductoSeleccionado!.Nombre);
    }

    [AvaloniaFact]
    public void CantidadInvalida_MuestraElErrorBajoElCampoYNoMueveElMotivo()
    {
        var m = Montar(salida: true);
        Completar(m, "Cable 2x1,5 mm", 3m, MotivoMovimiento.UsoOConsumo);
        var caja = MontajeV2.Parte<CampoFormulario>(m.Vista, "CampoCantidad").GetVisualDescendants().OfType<TextBox>().First();
        var yMotivo = Campo(m, "CampoMotivo").Y;

        caja.Focus();
        caja.Text = "5.4";
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(caja.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "Usá coma para los decimales: 5,4" && ArbolVisual.EsVisibleEnArbol(t));
        Assert.Equal(yMotivo, Campo(m, "CampoMotivo").Y);
        Assert.False(MontajeV2.Parte<Button>(m.Vista, "BotonRegistrar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Combos_ProductoConXEntreDigitos_SinCalt()
    {
        var m = Montar(salida: true);
        Completar(m, "Cable 2x1,5 mm", 3m, MotivoMovimiento.UsoOConsumo);

        var texto = MontajeV2.Parte<ComboBox>(m.Vista, "ComboProducto").GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == "Cable 2x1,5 mm");
        var rasgos = texto.GetValue(TextElement.FontFeaturesProperty);

        Assert.NotNull(rasgos);
        Assert.Contains(rasgos!, f => f.Tag == "calt" && f.Value == 0);
    }
}
