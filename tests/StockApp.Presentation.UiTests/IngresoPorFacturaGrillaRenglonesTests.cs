using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Repro de DOS hallazgos de verificación visual reales sobre la grilla de renglones de
/// <c>IngresoPorFacturaView.axaml</c> (screenshot real de la app corriendo, WSLg, fila "Arroz
/// Gallo Oro 1kg" con Cantidad=5, PrecioUnitario=5.4):
///
/// BUG 1 — el botón "Quitar" (última columna, <c>DataGridTemplateColumn</c>) se renderiza como
/// una píldora VACÍA: se ve el borde redondeado pero sin texto. Causa: es el ÚNICO botón de
/// ACCIÓN de TODA la app (barrido completo de <c>Views/*/*.axaml</c>: ~110 botones) sin ninguna
/// clase del UI Kit (<c>primary</c>/<c>secondary</c>/<c>ghost</c>/<c>danger</c>) — los otros DOS
/// botones "Quitar" reales del proyecto (<c>AdjuntosDocumentoPanelView.axaml</c>,
/// <c>AdjuntosPanelView.axaml</c>) usan <c>Classes="secondary"</c> y se ven bien. Fix: aplicar
/// exactamente ese mismo patrón acá.
///
/// BUG 2 — en la MISMA fila, "Precio unitario" se mostraba "5,4" (coma, vía
/// <see cref="StockApp.Presentation.Converters.DecimalConverter"/>, que fija cultura es-UY) y
/// "Subtotal" se mostraba "27.0" (punto, porque esa columna NO tenía converter y caía en el
/// binding default bajo la cultura ambiente del hilo, distinta de es-UY en esta máquina).
/// Decisión del usuario: unificar TODA la grilla con PUNTO. Fix: converter nuevo
/// <see cref="StockApp.Presentation.Converters.DecimalPuntoConverter"/> (cultura invariante,
/// punto), cableado en Cantidad/Precio unitario/Subtotal de la grilla y en Cantidad/Precio
/// unitario de la zona de carga (los campos que alimentan esas columnas). En su momento se dejó
/// deliberadamente sin tocar <c>DecimalConverter</c> (coma/es-UY) porque su único consumidor era
/// <c>NuevoProductoPrecioVenta</c> ("Precio de venta" del alta rápida de producto) — territorio de
/// otro frente en paralelo. Ese frente resultó ser la eliminación completa de
/// <c>Producto.PrecioVenta</c> (decisión de producto: el cliente no vende); al desaparecer el
/// campo, <c>DecimalConverter</c> quedó sin ningún consumidor y se borró junto con este comentario
/// histórico, que se conserva solo para documentar la secuencia.
/// </summary>
public class IngresoPorFacturaGrillaRenglonesTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:mov="clr-namespace:StockApp.Presentation.Views.Movimientos;assembly=GestionMunicipal"
                Width="1000" Height="1600">
            <mov:IngresoPorFacturaView />
        </Window>
        """;

    private static ProductoDto Producto(int id, string nombre, decimal precioCosto = 100m) => new(
        id, $"COD{id}", null, nombre, null, null, null, null, 1, "Unidad",
        precioCosto, 10m, 1m, true, DateTime.Today);

    private static (Window Window, IngresoPorFacturaViewModel Vm, FilaRenglonFacturaVm Fila) MontarConUnRenglon(
        decimal cantidad, decimal precioUnitario)
    {
        var producto = Producto(1, "Arroz Gallo Oro 1kg");
        var servicio = new IngresoPorFacturaServiceFake();
        var productoService = new ProductoServiceIngresoFake(new List<ProductoDto> { producto });
        var categoriaService = new CategoriaServiceFake(Array.Empty<Categoria>());
        var unidadService = new UnidadMedidaServiceFake(Array.Empty<UnidadMedida>());
        var proveedorService = new ProveedorServiceFake(Array.Empty<Proveedor>());
        var fuenteService = new FuenteFinanciamientoServiceFake(Array.Empty<FuenteFinanciamiento>());
        var rubroService = new RubroGastoServiceFake(Array.Empty<RubroGasto>());
        var lineaService = new LineaPoaServiceFake(Array.Empty<LineaPoa>());
        var nav = new NavigationRecorderFake();
        var confirmacion = new ConfirmacionServiceFake();
        var adjuntosPanel = new StockApp.Presentation.ViewModels.Finanzas.AdjuntosPanelViewModel(
            new AdjuntoServiceFake(), new ServicioSeleccionArchivoFake(), new ServicioAperturaArchivoFake(),
            confirmacion, new SesionFake(RolUsuario.Admin));

        var vm = new IngresoPorFacturaViewModel(
            servicio, productoService, categoriaService, unidadService, proveedorService,
            fuenteService, rubroService, lineaService, nav, confirmacion, adjuntosPanel);

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var fila = new FilaRenglonFacturaVm
        {
            Producto = producto,
            Cantidad = cantidad,
            PrecioUnitario = precioUnitario,
            ActualizarPrecioCosto = false,
        };
        vm.Renglones.Add(fila);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm, fila);
    }

    /// <summary>
    /// BUG 1. Antes del fix, este assert falla con Classes=[] (el botón real de la vista no
    /// lleva ninguna clase del UI Kit) -- ver mutación documentada en el reporte de la tarea.
    /// </summary>
    [AvaloniaFact]
    public void BotonQuitar_TieneClaseSecondaryDelUiKit_MismoPatronQueLosOtrosBotonesQuitarDeLaApp()
    {
        var (window, _, fila) = MontarConUnRenglon(cantidad: 5m, precioUnitario: 5.4m);

        var botonQuitar = window.GetVisualDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, "Quitar") && ReferenceEquals(b.DataContext, fila));

        Assert.Contains("secondary", botonQuitar.Classes);

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// BUG 2. Antes del fix: "PrecioUnitario" renderiza "5,4" (coma, vía <c>DecimalConverter</c>,
    /// que fija es-UY) y "Subtotal" renderiza "27.0" (punto: esa columna NO tenía converter y
    /// caía en el binding default de Avalonia bajo la cultura AMBIENTE del hilo) -- inconsistentes
    /// en la MISMA fila. Después del fix ambos deben usar PUNTO y ninguno debe tener coma.
    ///
    /// Cultura AMBIENTE fijada EXPLÍCITAMENTE a es-UY (coma): sin esto, el test sería un falso
    /// negativo ante la mutación "sacar el converter de Subtotal" en esta máquina, donde la
    /// cultura ambiente del proceso que corre la suite YA formatea con punto por default --
    /// mutar y sacar el converter igual daría "27.0" por casualidad del entorno, no porque el
    /// fix esté funcionando. Con es-UY forzada, sin converter el binding default cae en coma
    /// ("27,0") -- ahí sí la mutación se ve.
    /// </summary>
    [AvaloniaFact]
    public void Cantidad_PrecioUnitario_Y_Subtotal_SeRenderizanConPuntoYSinComas_EnLaMismaFila()
    {
        var culturaOriginal = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = ObtenerCulturaEsUyOEsAr();
        try
        {
            var (window, _, fila) = MontarConUnRenglon(cantidad: 5m, precioUnitario: 5.4m);

            var row = window.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, fila));
            var textos = row.GetVisualDescendants().OfType<TextBlock>()
                .Where(tb => ReferenceEquals(tb.DataContext, fila))
                .Select(tb => tb.Text)
                .ToList();

            // Producto/Cantidad/PrecioUnitario/Subtotal, en orden de columna (ver comentario de
            // MontarConUnRenglon). Verificado explícitamente por contenido en vez de solo por
            // índice, para no depender ciegamente del orden de recorrido del árbol visual.
            var textoCantidad = Assert.Single(textos, t => t == "5");
            var textoPrecio = Assert.Single(textos, t => t is not null && (t.StartsWith("5,", StringComparison.Ordinal) || t.StartsWith("5.", StringComparison.Ordinal)));
            var textoSubtotal = Assert.Single(textos, t => t is not null && (t.StartsWith("27,", StringComparison.Ordinal) || t.StartsWith("27.", StringComparison.Ordinal)));

            Assert.Equal("5.4", textoPrecio);
            Assert.Equal("27.0", textoSubtotal);
            Assert.DoesNotContain(",", textoCantidad);
            Assert.DoesNotContain(",", textoPrecio);
            Assert.DoesNotContain(",", textoSubtotal);

            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culturaOriginal;
        }
    }

    /// <summary>Mismo criterio de cultura fallback que MonedaConverter/CantidadConverter/etc.:
    /// si "es-UY" no está instalada en el runtime, se usa "es-AR" (mismos separadores) como
    /// segunda opción antes de fallar el test por un motivo ajeno al bug bajo prueba.</summary>
    private static System.Globalization.CultureInfo ObtenerCulturaEsUyOEsAr()
    {
        try { return System.Globalization.CultureInfo.GetCultureInfo("es-UY"); }
        catch (System.Globalization.CultureNotFoundException) { return System.Globalization.CultureInfo.GetCultureInfo("es-AR"); }
    }

    /// <summary>
    /// BUG 3 (guardián de NO-REGRESIÓN, reemplaza el test de diagnóstico
    /// DiagnosticoBotonQuitarDataGridTests.cs que hizo su trabajo y se borró). Causa raíz medida y
    /// documentada en el comentario XAML de <c>DataGridCell.sin-padding-vertical</c>
    /// (IngresoPorFacturaView.axaml): RowHeight=36 - PaddingCelda vertical (16) deja sólo 20px
    /// útiles en la celda, y el Button (Padding 16,8 + BorderThickness 1) necesita ~36px -- el
    /// TextBlock interno queda aplastado a 2px de alto. El fix saca el padding vertical de la
    /// CELDA (no del botón) vía <c>CellStyleClasses="sin-padding-vertical"</c> en la columna.
    ///
    /// Deliberadamente NO se assertea que la columna tenga la clase puesta en el XAML: eso no
    /// custodia el RENDER (mismo criterio que "Test de VM no custodia gate de UI" -- un gate
    /// sacado del XAML puede dejar en verde un test que sólo mira la existencia de la clase).
    /// Este test mide el alto REAL del TextBlock ya renderizado dentro de la celda real del
    /// DataGrid.
    /// </summary>
    [AvaloniaFact]
    public void BotonQuitar_TextoInterno_TieneAltoRenderizadoVisible_DentroDeLaCeldaDelDataGrid()
    {
        var (window, _, fila) = MontarConUnRenglon(cantidad: 5m, precioUnitario: 5.4m);

        var botonQuitar = window.GetVisualDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, "Quitar") && ReferenceEquals(b.DataContext, fila));
        var textoDelBoton = botonQuitar.GetVisualDescendants().OfType<TextBlock>().Single();

        Assert.True(textoDelBoton.Bounds.Height > 10,
            $"El TextBlock interno del botón 'Quitar' mide {textoDelBoton.Bounds.Height}px de " +
            "alto dentro de la celda del DataGrid -- el texto está aplastado (esperable si la " +
            "celda conserva su padding vertical de 8px: RowHeight 36 - 16 = 20px útiles, " +
            "insuficientes para el botón). Verificar que la DataGridTemplateColumn tenga " +
            "CellStyleClasses=\"sin-padding-vertical\" (ver IngresoPorFacturaView.axaml).");

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
