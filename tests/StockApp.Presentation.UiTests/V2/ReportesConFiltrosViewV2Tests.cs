using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Auth;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.Views.Reportes;
using Xunit;
using static StockApp.Presentation.UiTests.V2.DatosDeReportes;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Más movidos, Historial por producto, Log de auditoría y Estadística de tareas v2 con la View REAL: raíz, orden y anchos
/// de los filtros, error de fechas, vacío sin producto, combo de usuarios, sin conexión al abrir y el criterio de cierre.</summary>
public class ReportesConFiltrosViewV2Tests
{
    private static T Parte<T>(Control raiz, string nombre) where T : Control => MontajeV2.Parte<T>(raiz, nombre);
    private static bool Visible(Control c) => ArbolVisual.EsVisibleEnArbol(c);

    private static MasMovidoDto Mas(int id, string nombre) => new(id, $"P{id:000}", nombre, 10, 100m);
    private static FilaReporteTareas Fila(string zona) => new(zona, 1, 2, 3, 4, 10);
    private static UsuarioDto Usuario(int id, string nombre) => new(id, nombre, null, RolUsuario.Operador, true, default);

    private static Control VistaDe(Type tipo) => tipo.Name switch
    {
        nameof(MasMovidosView) => MontajeReportes.MasMovidos(new ReporteStockFake { MasMovidos = { Mas(1, "Cemento") } }).Vista,
        nameof(HistorialPorProductoView) => MontajeReportes.Historial(new ReporteStockFake { Historial = { Movimiento(1, 10m, 5m) } }).Vista,
        nameof(AuditoriaLogView) => MontajeReportes.Auditoria(new AuditoriaQueryFake(), new UsuariosListaFake { Lista = { Usuario(2, "mlopez") } }).Vista,
        _ => MontajeReportes.Tareas(new ReporteTareasFake { Filas = { Fila("Zona Centro") } }).Vista,
    };

    private static string TituloDe(Type tipo) => tipo.Name switch
    {
        nameof(MasMovidosView) => "Productos más movidos",
        nameof(HistorialPorProductoView) => "Historial por producto",
        nameof(AuditoriaLogView) => "Log de auditoría",
        _ => "Estadística de tareas",
    };

    [AvaloniaTheory]
    [InlineData(typeof(MasMovidosView))]
    [InlineData(typeof(HistorialPorProductoView))]
    [InlineData(typeof(AuditoriaLogView))]
    [InlineData(typeof(ReporteTareasView))]
    public void Raiz_TieneUi2_BreadcrumbReportes_BuscarEnLaBarra_ExportarEnElEncabezado_SinPrimario(Type tipo)
    {
        var vista = VistaDe(tipo);

        Assert.Contains("ui2", vista.Classes);
        var encabezado = Parte<EncabezadoPagina>(vista, "Encabezado");
        Assert.Equal("Reportes", encabezado.BreadcrumbOrigen);
        Assert.Equal(TituloDe(tipo), encabezado.BreadcrumbActual);
        var buscar = Parte<Button>(vista, "BotonBuscar");
        var exportar = Parte<Button>(vista, "BotonExportarPdf");
        Assert.Contains("secundario", buscar.Classes);
        Assert.Contains("secundario", exportar.Classes);
        Assert.Same(Parte<Button>(vista, "BotonExportarPdf").Command, ((dynamic)vista.DataContext!).ExportarPdfCommand);
        Assert.Contains(exportar, encabezado.GetVisualDescendants().OfType<Button>());
        Assert.DoesNotContain(buscar, encabezado.GetVisualDescendants().OfType<Button>());
        Assert.DoesNotContain(vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
        Assert.DoesNotContain(vista.GetVisualDescendants().OfType<Button>(), b => (b.Content as string) == "Actualizar");
    }

    private static List<(string Etiqueta, double Ancho)> Campos(Control vista) =>
        Parte<BarraFiltros>(vista, "BarraDeFiltros").GetVisualDescendants().OfType<CampoFormulario>()
            .Select(c => (c.Etiqueta ?? "", ((Control)c.Content!).Width)).ToList();

    [AvaloniaFact]
    public void Filtros_MasMovidos_EnElOrdenAprobado_ConEtiquetaArriba()
    {
        var vista = VistaDe(typeof(MasMovidosView));

        Assert.Equal([("Desde", 140d), ("Hasta", 140d), ("Top", 100d)], Campos(vista));
    }

    [AvaloniaFact]
    public void Filtros_Historial_EnElOrdenAprobado_ConEtiquetaArriba()
    {
        var vista = VistaDe(typeof(HistorialPorProductoView));

        Assert.Equal([("Producto", 280d), ("Desde", 140d), ("Hasta", 140d)], Campos(vista));
    }

    [AvaloniaFact]
    public void Filtros_Auditoria_EnElOrdenAprobado_ConEtiquetaArriba()
    {
        var vista = VistaDe(typeof(AuditoriaLogView));

        Assert.Equal([("Usuario", 200d), ("Desde", 140d), ("Hasta", 140d)], Campos(vista));
    }

    [AvaloniaFact]
    public void Filtros_Tareas_EnElOrdenAprobado_ConEtiquetaArriba()
    {
        var vista = VistaDe(typeof(ReporteTareasView));

        Assert.Equal([("Agrupar por", 220d), ("Contar por", 180d), ("Desde", 140d), ("Hasta", 140d)], Campos(vista));
    }

    [AvaloniaTheory]
    [InlineData(typeof(MasMovidosView))]
    [InlineData(typeof(HistorialPorProductoView))]
    [InlineData(typeof(AuditoriaLogView))]
    [InlineData(typeof(ReporteTareasView))]
    public void Buscar_EsSecundarioYEstaAlFinalDeLaBarra(Type tipo)
    {
        var vista = VistaDe(tipo);

        var buscar = Parte<Button>(vista, "BotonBuscar");
        Assert.Contains("secundario", buscar.Classes);
        var xBuscar = buscar.TranslatePoint(default, vista)!.Value.X;
        foreach (var campo in Parte<BarraFiltros>(vista, "BarraDeFiltros").GetVisualDescendants().OfType<CampoFormulario>())
            Assert.True(campo.TranslatePoint(default, vista)!.Value.X < xBuscar, campo.Etiqueta);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Bottom, buscar.VerticalAlignment);
    }

    [AvaloniaFact]
    public void FechasInvertidas_AlertaDeErrorDebajoDeLosFiltros_SinTocarLaGrilla()
    {
        var m = MontajeReportes.MasMovidos(new ReporteStockFake { MasMovidos = { Mas(1, "Cemento"), Mas(2, "Arena") } });
        var filasAntes = m.Vm.Items;
        var alerta = Parte<Alerta>(m.Vista, "AlertaError");
        Assert.False(Visible(alerta));

        m.Vm.FechaDesde = new DateTime(2026, 9, 30);
        m.Vm.FechaHasta = new DateTime(2026, 9, 1);
        MontajeV2.Clic(m.Ventana, Parte<Button>(m.Vista, "BotonBuscar"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(Visible(alerta));
        Assert.Equal("La fecha 'Desde' no puede ser posterior a 'Hasta'.", alerta.Texto);
        Assert.Same(filasAntes, m.Vm.Items);
        Assert.True(Visible(Parte<DataGrid>(m.Vista, "Grilla")));
        var yFiltros = Parte<BarraFiltros>(m.Vista, "BarraDeFiltros").TranslatePoint(default, m.Vista)!.Value.Y;
        Assert.True(alerta.TranslatePoint(default, m.Vista)!.Value.Y > yFiltros);
    }

    [AvaloniaFact]
    public void HistorialSinProducto_VacioElegiUnProducto_SinConsultar_PdfDeshabilitado()
    {
        var servicio = new ReporteStockFake { Historial = { Movimiento(1, 10m, 5m) } };
        var m = MontajeReportes.Historial(servicio);

        var vacio = Parte<EstadoVacio>(m.Vista, "EstadoVacio");
        Assert.True(Visible(vacio));
        Assert.Equal("Elegí un producto", vacio.Titulo);
        Assert.Equal("Buscá un producto por código o nombre para ver sus movimientos.", vacio.Mensaje);
        Assert.Empty(servicio.ConsultasHistorial);
        Assert.False(Parte<Button>(m.Vista, "BotonExportarPdf").IsEffectivelyEnabled);
        Assert.True(Visible(Parte<CampoFormulario>(m.Vista, "CampoProducto")));
    }

    [AvaloniaFact]
    public void HistorialConProducto_Buscar_MuestraLosMovimientos_YStockNuevoNegativoConInsignia()
    {
        var servicio = new ReporteStockFake { Historial = { Movimiento(2, 25m, -20m, "Reposición de emergencia"), Movimiento(1, 100m, 25m) } };
        var m = MontajeReportes.Historial(servicio);
        m.Vm.ProductoSeleccionado = Producto(1, "Cemento portland 50 kg");

        MontajeV2.Clic(m.Ventana, Parte<Button>(m.Vista, "BotonBuscar"));
        Dispatcher.UIThread.RunJobs();

        Assert.Single(servicio.ConsultasHistorial);
        Assert.Equal(1, servicio.ConsultasHistorial[0].ProductoId);
        Assert.Equal(2, m.Vm.Items.Count);
        Assert.Equal("2 movimientos", Parte<TextBlock>(m.Vista, "PieConteo").Text);
        var insignia = Assert.Single(m.Vista.GetVisualDescendants().OfType<Insignia>().Where(i => i.Texto == "Stock negativo"), Visible);
        Assert.Equal(VarianteInsignia.Peligro, insignia.Variante);
        Assert.Contains(m.Vista.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "-20" && t.Classes.Contains("negativo") && Visible(t));
        Assert.True(Parte<Button>(m.Vista, "BotonExportarPdf").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Auditoria_ComboUsuarioConTodosPrimero()
    {
        var m = MontajeReportes.Auditoria(new AuditoriaQueryFake(), new UsuariosListaFake { Lista = { Usuario(2, "mlopez"), Usuario(3, "jrodriguez") } });

        var combo = Parte<ComboBox>(m.Vista, "ComboUsuario");
        var nombres = combo.Items.OfType<OpcionUsuario>().Select(o => o.Nombre).ToList();
        Assert.Equal(["Todos", "mlopez", "jrodriguez"], nombres);
        Assert.Equal("Todos", ((OpcionUsuario)combo.SelectedItem!).Nombre);
    }

    [AvaloniaFact]
    public void Auditoria_SinConexionAlAbrir_EstadoEnLineaConReintentar_YReintentarCargaElCombo()
    {
        // Review Focus 5: la API está caída y el usuario abre Log de auditoría.
        var usuarios = new UsuariosListaFake { Falla = new ServidorNoDisponibleException() };
        var servicio = new AuditoriaQueryFake { Registros = { new(new DateTime(2026, 10, 7, 14, 35, 0, DateTimeKind.Utc), "mperez", AccionAuditada.CambioPrecio, "Producto", 1, "5 -> 8") } };
        var m = MontajeReportes.Auditoria(servicio, usuarios);

        Assert.True(Visible(Parte<EstadoVacio>(m.Vista, "EstadoSinConexion")));
        Assert.True(Visible(Parte<BarraFiltros>(m.Vista, "BarraDeFiltros")));
        Assert.Empty(m.Confirmacion.MensajesInformados);
        Assert.Empty(m.Vm.Usuarios);

        usuarios.Falla = null;
        usuarios.Lista.Add(Usuario(2, "mlopez"));
        MontajeV2.Clic(m.Ventana, Parte<Button>(m.Vista, "BotonReintentar"));
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Parte<EstadoVacio>(m.Vista, "EstadoSinConexion")));
        Assert.Equal(2, m.Vm.Usuarios.Count);
        Assert.Single(m.Vm.Items);
        Assert.Equal(2, Parte<ComboBox>(m.Vista, "ComboUsuario").Items.Count);
    }

    [AvaloniaFact]
    public void Auditoria_LaAccionSeMuestraEnTextoLegible()
    {
        var servicio = new AuditoriaQueryFake { Registros = { new(new DateTime(2026, 10, 7, 14, 35, 0, DateTimeKind.Utc), "mperez", AccionAuditada.RegistroMovimiento, "MovimientoStock", 209, "Entrada de 40 unidades") } };
        var m = MontajeReportes.Auditoria(servicio, new UsuariosListaFake());

        var textos = m.Vista.GetVisualDescendants().OfType<TextBlock>().Where(Visible).Select(t => t.Text).ToList();
        Assert.Contains("Registro de movimiento", textos);
        Assert.DoesNotContain("RegistroMovimiento", textos);
    }

    [AvaloniaFact]
    public void Tareas_ElegirFechaDeCierre_MuestraElAviso_YVolverACreacionLoOculta()
    {
        var m = MontajeReportes.Tareas(new ReporteTareasFake { Filas = { Fila("Zona Centro") } });
        var aviso = Parte<Alerta>(m.Vista, "AlertaCriterioCierre");
        var combo = Parte<ComboBox>(m.Vista, "ComboCriterio");
        Assert.False(Visible(aviso));

        combo.SelectedItem = m.Vm.CriteriosDisponibles[1];
        Dispatcher.UIThread.RunJobs();

        Assert.True(m.Vm.EsCriterioCierre);
        Assert.True(Visible(aviso));
        Assert.Equal(VarianteAlerta.Advertencia, aviso.Variante);
        Assert.StartsWith("Criterio 'Fecha de cierre'", aviso.Texto);

        combo.SelectedItem = m.Vm.CriteriosDisponibles[0];
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(aviso));
    }

    [AvaloniaFact]
    public void Tareas_PieConTotalGeneral()
    {
        var m = MontajeReportes.Tareas(new ReporteTareasFake { Filas = { Fila("Zona Centro"), Fila("Zona Norte") } });

        Assert.Equal("2 filas", Parte<TextBlock>(m.Vista, "PieConteo").Text);
        var total = Parte<StackPanel>(m.Vista, "PieTotalGeneral");
        Assert.True(Visible(total));
        var textos = total.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Total general", textos);
        Assert.Contains("20", textos);
    }

    [AvaloniaFact]
    public void Tareas_SinRadioButtons()
    {
        var m = MontajeReportes.Tareas(new ReporteTareasFake { Filas = { Fila("Zona Centro") } });

        Assert.Empty(m.Vista.GetVisualDescendants().OfType<RadioButton>());
        Assert.NotEmpty(Parte<ComboBox>(m.Vista, "ComboAgrupador").Items);
    }

    [AvaloniaFact]
    public void Auditoria_OperadorConReportesVer_VeElLog_SinPermisoDeUsuarios_YSinElComboUsuario()
    {
        // GET /usuarios exige usuarios.gestionar (estructural de Admin): el fake da 403 a un Operador, como el servidor.
        var usuarios = new UsuariosListaFake { Lista = { Usuario(2, "mlopez") } };
        var servicio = new AuditoriaQueryFake { Registros = { new(new DateTime(2026, 10, 7, 14, 35, 0, DateTimeKind.Utc), "mperez", AccionAuditada.CambioPrecio, "Producto", 1, "5 -> 8") } };

        var m = MontajeReportes.Auditoria(servicio, usuarios, sesion: new SesionFake(RolUsuario.Operador, StockApp.Application.Authorization.Permisos.VerReportes));

        Assert.False(m.Vm.SinPermiso);
        Assert.False(Visible(Parte<EstadoVacio>(m.Vista, "EstadoSinConexion")));
        Assert.Equal(0, usuarios.Llamadas);
        Assert.Single(m.Vm.Items);
        Assert.True(Visible(Parte<DataGrid>(m.Vista, "Grilla")));
        Assert.False(Visible(Parte<CampoFormulario>(m.Vista, "CampoUsuario")));
        Assert.True(Visible(Parte<Button>(m.Vista, "BotonBuscar")));
        Assert.True(Visible(Parte<Button>(m.Vista, "BotonExportarPdf")));
    }

    [AvaloniaFact]
    public void Auditoria_Admin_VeElComboUsuario()
    {
        var m = MontajeReportes.Auditoria(new AuditoriaQueryFake(), new UsuariosListaFake { Lista = { Usuario(2, "mlopez") } });

        Assert.True(Visible(Parte<CampoFormulario>(m.Vista, "CampoUsuario")));
    }
}
