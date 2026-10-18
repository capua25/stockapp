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
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.Views.Reportes;
using Xunit;
using static StockApp.Presentation.UiTests.V2.DatosDeReportes;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Valorización de inventario y Stock por categoría v2 con la View REAL: raíz y encabezado, columnas, stock negativo,
/// pie y total, Exportar PDF según haya filas, Actualizar, 403 dentro del shell y columnas quietas al scrollear.</summary>
public class ReportesInventarioViewV2Tests
{
    private static T Parte<T>(Control raiz, string nombre) where T : Control => MontajeV2.Parte<T>(raiz, nombre);
    private static bool Visible(Control c) => ArbolVisual.EsVisibleEnArbol(c);

    private static StockCategoriaDto Categoria(string nombre, decimal stock) => new(nombre, 2, stock, stock * 100m);

    private static ReporteStockFake ConNegativo() => new()
    {
        Valorizacion = { Cemento, Hipoclorito },
        Categorias = { Categoria("Construcción", 365m), Categoria("Limpieza", -3m) },
    };

    private static (Control Vista, Window Ventana) Montar(Type vista, ReporteStockFake servicio)
    {
        if (vista == typeof(ValorizacionView))
        {
            var m = MontajeReportes.Valorizacion(servicio);
            return (m.Vista, m.Ventana);
        }
        var s = MontajeReportes.StockCategoria(servicio);
        return (s.Vista, s.Ventana);
    }

    [AvaloniaTheory]
    [InlineData(typeof(ValorizacionView))]
    [InlineData(typeof(StockCategoriaView))]
    public void Raiz_TieneUi2_BreadcrumbReportes_ActualizarYExportarSecundarios_SinPrimario(Type tipo)
    {
        var (vista, _) = Montar(tipo, ConNegativo());

        Assert.Contains("ui2", vista.Classes);
        var encabezado = Parte<EncabezadoPagina>(vista, "Encabezado");
        Assert.Equal("Reportes", encabezado.BreadcrumbOrigen);
        Assert.Equal(tipo == typeof(ValorizacionView) ? "Valorización de inventario" : "Stock por categoría", encabezado.BreadcrumbActual);
        var actualizar = Parte<Button>(vista, "BotonActualizar");
        var exportar = Parte<Button>(vista, "BotonExportarPdf");
        Assert.Contains("secundario", actualizar.Classes);
        Assert.Contains("secundario", exportar.Classes);
        Assert.Equal("Actualizar", actualizar.Content);
        Assert.Equal("Exportar PDF", exportar.Content);
        Assert.DoesNotContain(vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
    }

    [AvaloniaFact]
    public void Valorizacion_SeisColumnasEnMayusculas_EnElOrdenAprobado()
    {
        var m = MontajeReportes.Valorizacion(ConNegativo());

        var columnas = Parte<DataGrid>(m.Vista, "Grilla").Columns.Select(c => c.Header as string).ToList();
        Assert.Equal(["CÓDIGO", "NOMBRE", "CATEGORÍA", "STOCK", "P. COSTO", "VALOR COSTO"], columnas);
    }

    [AvaloniaFact]
    public void StockCategoria_CuatroColumnasEnMayusculas_EnElOrdenAprobado()
    {
        var m = MontajeReportes.StockCategoria(ConNegativo());

        var columnas = Parte<DataGrid>(m.Vista, "Grilla").Columns.Select(c => c.Header as string).ToList();
        Assert.Equal(["CATEGORÍA", "PRODUCTOS", "STOCK TOTAL", "VALOR COSTO"], columnas);
    }

    [AvaloniaTheory]
    [InlineData(typeof(ValorizacionView), "-3")]
    [InlineData(typeof(StockCategoriaView), "-3")]
    public void StockNegativo_InsigniaPeligroYNumeroNegativo_SoloEnEsaFila(Type tipo, string textoNegativo)
    {
        var (vista, _) = Montar(tipo, ConNegativo());

        var insignias = vista.GetVisualDescendants().OfType<Insignia>().Where(i => i.Texto == "Stock negativo").ToList();
        var visible = Assert.Single(insignias, Visible);
        Assert.Equal(VarianteInsignia.Peligro, visible.Variante);
        var numeros = vista.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("negativo") && Visible(t)).ToList();
        var numero = Assert.Single(numeros);
        Assert.Equal(textoNegativo, numero.Text);
    }

    [AvaloniaFact]
    public void Pie_ConteoYTotalValorCosto()
    {
        var m = MontajeReportes.Valorizacion(ConNegativo());

        Assert.Equal("2 productos", Parte<TextBlock>(m.Vista, "PieConteo").Text);
        var total = Parte<StackPanel>(m.Vista, "PieTotal");
        Assert.True(Visible(total));
        var textos = total.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Total valor costo", textos);
        Assert.Contains("$ 152.970,00", textos);   // 153.600 - 630

        var stock = MontajeReportes.StockCategoria(ConNegativo());
        Assert.Equal("2 categorías", Parte<TextBlock>(stock.Vista, "PieConteo").Text);
    }

    [AvaloniaFact]
    public void ExportarPdf_DeshabilitadoSinFilas_HabilitadoConFilas()
    {
        var vacio = MontajeReportes.Valorizacion(new ReporteStockFake());
        Assert.False(Parte<Button>(vacio.Vista, "BotonExportarPdf").IsEffectivelyEnabled);

        var conFilas = MontajeReportes.Valorizacion(ConNegativo());
        Assert.True(Parte<Button>(conFilas.Vista, "BotonExportarPdf").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Actualizar_RecargaElReporte()
    {
        var servicio = ConNegativo();
        var m = MontajeReportes.Valorizacion(servicio);
        Assert.Equal(1, servicio.LlamadasValorizacion);

        MontajeV2.Clic(m.Ventana, Parte<Button>(m.Vista, "BotonActualizar"));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, servicio.LlamadasValorizacion);
    }

    [AvaloniaFact]
    public async Task Un403AlActualizar_DentroDelShell_SinPermisoPdfDeshabilitadoYSinItemReportes()
    {
        // Review Focus 4: Admin le quita reportes.ver a un operador con Valorización abierta; el operador toca Actualizar.
        var sesion = new SesionFake(RolUsuario.Operador, Permisos.VerReportes);
        var tras = new HashSet<string>();
        var servicio = ConNegativo();
        var confirmacion = new ConfirmacionServiceFake();
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), confirmacion,
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        foreach (var grupo in shell.Grupos)
            grupo.EstaExpandido = true;
        var vm = new ValorizacionViewModel(servicio, new GuardadoSinUsoFake(), confirmacion, new PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake(),
            sesion, new NavigationServiceFake());
        shell.CurrentContent = vm;
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
        var vista = ventana.GetVisualDescendants().OfType<ValorizacionView>().Single();
        Assert.True(Visible(Parte<Button>(vista, "BotonExportarPdf")));
        Assert.True(shell.Grupos.Single(g => g.Titulo == "ANÁLISIS").Items.Single().EsVisible);

        servicio.FallaValorizacion = new UnauthorizedAccessException();
        MontajeV2.Clic(ventana, Parte<Button>(vista, "BotonActualizar"));
        Dispatcher.UIThread.RunJobs();
        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), confirmacion, shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.SinPermiso);
        Assert.True(Visible(Parte<EstadoVacio>(vista, "EstadoSinPermiso")));
        Assert.False(Visible(Parte<TarjetaContenido>(vista, "Tarjeta")));
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
        Assert.False(shell.Grupos.Single(g => g.Titulo == "ANÁLISIS").Items.Single().EsVisible);
        Assert.Single(confirmacion.MensajesInformados);   // un solo Aviso: el global del 403, no uno por pantalla
    }

    [AvaloniaFact]
    public void Scroll_HastaLaFilaNegativa_NoMueveNingunaColumna()
    {
        // Convención 14: una columna Auto no puede crecer cuando entra en pantalla una fila más ancha.
        var servicio = new ReporteStockFake();
        for (var i = 0; i < 60; i++)
            servicio.Valorizacion.Add(Item(i + 1, $"C{i:000}", $"Producto {i}", 5m + i, 100m));
        servicio.Valorizacion.Add(Item(99, "LIM-0455", "Hipoclorito de sodio 5 L", -99999.5m, 210m, "Limpieza"));
        var m = MontajeReportes.Valorizacion(servicio);
        var grilla = Parte<DataGrid>(m.Vista, "Grilla");
        var antes = grilla.Columns.Select(c => c.ActualWidth).ToList();

        grilla.ScrollIntoView(m.Vm.Items.Last(), grilla.Columns[0]);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var despues = grilla.Columns.Select(c => c.ActualWidth).ToList();
        for (var i = 0; i < antes.Count; i++)
            Assert.Equal(antes[i], despues[i], 0.5);
        Assert.Contains(m.Vista.GetVisualDescendants().OfType<Insignia>(), i => i.Texto == "Stock negativo" && Visible(i));
    }
}
