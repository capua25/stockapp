using StockApp.Domain.Formato;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Reportes;

namespace StockApp.Presentation.VisualTests.Pantallas.Reportes;

/// <summary>Los mockups de reportes escriben estos textos a mano: si un dato cambia y el mockup no, la comparación falla
/// por un texto distinto y el diagnóstico no lo dice.</summary>
public class DatosReportesTests
{
    private static T Contenido<T>(ShellMainViewModel shell) where T : ViewModelBase => Assert.IsType<T>(shell.CurrentContent);

    [Fact]
    public void CadaFabrica_DejaElReporteCargadoSinEstadosDeError()
    {
        var valorizacion = Contenido<ValorizacionViewModel>(DatosReportes.Valorizacion());
        var stock = Contenido<StockCategoriaViewModel>(DatosReportes.StockCategoria());
        var historial = Contenido<HistorialPorProductoViewModel>(DatosReportes.HistorialProducto());
        var masMovidos = Contenido<MasMovidosViewModel>(DatosReportes.MasMovidos());
        var auditoria = Contenido<AuditoriaLogViewModel>(DatosReportes.Auditoria());
        var tareas = Contenido<ReporteTareasViewModel>(DatosReportes.Tareas());

        Assert.Equal(8, valorizacion.Items.Count);
        Assert.Equal(5, stock.Items.Count);
        Assert.Equal(9, historial.Items.Count);
        Assert.Equal(10, masMovidos.Items.Count);
        Assert.Equal(11, auditoria.Items.Count);
        Assert.Equal(5, tareas.Items.Count);
        foreach (ViewModelBase vm in new ViewModelBase[] { valorizacion, stock, historial, masMovidos, auditoria, tareas })
        {
            Assert.False(vm.Cargando);
            Assert.False(vm.SinPermiso);
            Assert.False(vm.SinConexion);
        }
    }

    [Fact]
    public void ValorizacionSinConexion_TieneSinConexion()
    {
        var vm = Contenido<ValorizacionViewModel>(DatosReportes.ValorizacionSinConexion());

        Assert.True(vm.SinConexion);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public void ValorizacionSinPermiso_TieneSinPermisoYMensaje()
    {
        var vm = Contenido<ValorizacionViewModel>(DatosReportes.ValorizacionSinPermiso());

        Assert.True(vm.SinPermiso);
        Assert.Equal("No tenés permiso para ver el reporte de valorización de inventario.", vm.MensajeSinPermiso);
    }

    [Fact]
    public void ValorizacionCargando_TieneCargandoYSinFilas()
    {
        var vm = Contenido<ValorizacionViewModel>(DatosReportes.ValorizacionCargando());

        Assert.True(vm.Cargando);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public void ValorizacionVacia_TieneLaCargaTerminadaSinFilas()
    {
        var vm = Contenido<ValorizacionViewModel>(DatosReportes.ValorizacionVacia());

        Assert.False(vm.Cargando);
        Assert.Empty(vm.Items);
        Assert.Equal("No hay productos para valorizar", vm.TituloVacio);
    }

    [Fact]
    public void MasMovidosErrorFechas_TieneElMensajeDeHoy()
    {
        var vm = Contenido<MasMovidosViewModel>(DatosReportes.MasMovidosErrorFechas());

        Assert.Equal("La fecha 'Desde' no puede ser posterior a 'Hasta'.", vm.MensajeError);
        Assert.Equal(10, vm.Items.Count);
        Assert.Equal("30/09/2026", vm.FechaDesde!.Value.ToString("dd/MM/yyyy"));
        Assert.Equal("01/09/2026", vm.FechaHasta!.Value.ToString("dd/MM/yyyy"));
    }

    [Fact]
    public void TareasCierre_EsCriterioCierre()
    {
        var vm = Contenido<ReporteTareasViewModel>(DatosReportes.TareasCierre());

        Assert.True(vm.EsCriterioCierre);
        Assert.Equal("Fecha de cierre", vm.CriterioOpcionSeleccionada.Nombre);
        Assert.All(vm.Items, f => Assert.Equal(0, f.Pendientes + f.EnCurso));
    }

    [Fact]
    public void Tareas_RangoFijoYTotalGeneral()
    {
        var vm = Contenido<ReporteTareasViewModel>(DatosReportes.Tareas());

        Assert.Equal("01/09/2026", vm.FechaDesde!.Value.ToString("dd/MM/yyyy"));
        Assert.Equal("31/10/2026", vm.FechaHasta!.Value.ToString("dd/MM/yyyy"));
        Assert.Equal("Zona", vm.AgrupadorSeleccionado.Nombre);
        Assert.Equal(95, vm.TotalGeneral);
        Assert.All(vm.Items, f => Assert.Equal(f.Pendientes + f.EnCurso + f.Terminadas + f.Canceladas, f.Total));
    }

    [Fact]
    public void HubOperador_SinHistorial()
    {
        var vm = Contenido<ReportesViewModel>(DatosReportes.HubOperador());

        Assert.True(vm.PuedeVerReportes);
        Assert.False(vm.PuedeVerHistorialPorProducto);
    }

    [Fact]
    public void HubAdmin_VeTodo()
    {
        var vm = Contenido<ReportesViewModel>(DatosReportes.HubAdmin());

        Assert.True(vm.PuedeVerReportes);
        Assert.True(vm.PuedeVerHistorialPorProducto);
    }

    [Fact]
    public void Valorizacion_TieneUnStockNegativo()
    {
        var vm = Contenido<ValorizacionViewModel>(DatosReportes.Valorizacion());

        var negativos = vm.Items.Where(i => i.StockActual < 0).ToList();
        Assert.Single(negativos);
        Assert.Equal("-3", CantidadFormato(negativos[0].StockActual));
        Assert.Equal("-$ 630,00", FormatoEsUy.Moneda(negativos[0].ValorCosto));
        Assert.Equal("$ 317.476,25", FormatoEsUy.Moneda(vm.Totales!.TotalValorCosto));
        Assert.Equal("$ 153.600,00", FormatoEsUy.Moneda(vm.Items[0].ValorCosto));
        foreach (var i in vm.Items)
            Assert.Equal(i.StockActual * i.PrecioCosto, i.ValorCosto);
    }

    [Fact]
    public void StockCategoria_TieneUnStockTotalNegativo()
    {
        var vm = Contenido<StockCategoriaViewModel>(DatosReportes.StockCategoria());

        Assert.Single(vm.Items, c => c.StockTotal < 0);
        Assert.Equal(DatosReportes.ItemsValorizacion.Sum(i => i.ValorCosto), vm.Items.Sum(c => c.ValorCosto));
        Assert.Equal(DatosReportes.ItemsValorizacion.Count, vm.Items.Sum(c => c.CantidadProductos));
    }

    [Fact]
    public void HistorialProducto_UnMovimientoDejaElStockNegativo()
    {
        var vm = Contenido<HistorialPorProductoViewModel>(DatosReportes.HistorialProducto());

        Assert.Single(vm.Items, m => m.StockNuevo < 0);
        // Cada movimiento arranca donde terminó el anterior (la lista viene del más nuevo al más viejo).
        for (var i = 0; i < vm.Items.Count - 1; i++)
            Assert.Equal(vm.Items[i + 1].StockNuevo, vm.Items[i].StockAnterior);
    }

    [Fact]
    public void HistorialSinProducto_PideElegirUnProducto()
    {
        var vm = Contenido<HistorialPorProductoViewModel>(DatosReportes.HistorialSinProducto());

        Assert.Empty(vm.Items);
        Assert.Equal("Elegí un producto", vm.TituloVacio);
    }

    private static string CantidadFormato(decimal valor) => FormatoEsUy.Cantidad(valor);
}
