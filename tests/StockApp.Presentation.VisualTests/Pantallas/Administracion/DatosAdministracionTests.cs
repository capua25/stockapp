using StockApp.Presentation.ViewModels.Administracion;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.VisualTests.Pantallas.Administracion;

/// <summary>Los mockups de administración escriben estos textos a mano: si un dato cambia y el mockup no, la comparación
/// falla por un texto distinto y el diagnóstico no lo dice.</summary>
public class DatosAdministracionTests
{
    private static async Task<T> Cargado<T>(Func<StockApp.Presentation.ViewModels.ShellMainViewModel> fabrica) where T : CatalogoListadoViewModel
    {
        var catalogos = (CatalogosViewModel)fabrica().CurrentContent!;
        await catalogos.CargarAsync();
        return (T)catalogos.CatalogoActual!;
    }

    [Fact]
    public async Task Categorias_SeisFilasYUnaInactiva()
    {
        var listado = await Cargado<CategoriaListViewModel>(DatosCatalogos.ConCategorias);

        Assert.Equal(6, listado.Filas.Count);
        var inactiva = Assert.Single(listado.Filas, f => !f.Activo);
        Assert.Equal("Herramientas fuera de uso", inactiva.Valor0);
        Assert.Equal("Inactiva", listado.TextoInactivo);
        Assert.Equal(6, listado.CantidadVisible);
    }

    [Fact]
    public async Task Proveedores_CuatroFilas_CincoColumnas_UnaDireccionLarga()
    {
        var listado = await Cargado<ProveedorListViewModel>(DatosCatalogos.ConProveedores);

        Assert.Equal(4, listado.Filas.Count);
        Assert.Equal(["TELÉFONO", "EMAIL", "DIRECCIÓN", "NOTAS"], listado.Columnas.Select(c => c.Encabezado));
        Assert.Equal("Ruta 21 km 262, esquina camino a Playa Fomento, Carmelo, Colonia", listado.Filas[1].Valor3);
        Assert.Equal("Inactivo", listado.TextoInactivo);
        Assert.False(listado.Filas[3].Activo);
    }

    [Fact]
    public async Task LineasPoa_CuatroLineas2026_UnaInactiva()
    {
        var listado = await Cargado<LineaPoaListViewModel>(DatosCatalogos.ConLineasPoa);

        Assert.Equal(4, listado.Filas.Count);
        Assert.All(listado.Filas, f => Assert.Equal("2026", f.Valor2));
        Assert.Single(listado.Filas, f => !f.Activo);
        Assert.Equal(["PROGRAMA", "EJERCICIO"], listado.Columnas.Select(c => c.Encabezado));
    }

    [Fact]
    public async Task Operador_VeInventarioYTareas_ConZonasAbierto()
    {
        var shell = DatosCatalogos.Operador();
        var catalogos = (CatalogosViewModel)shell.CurrentContent!;
        await catalogos.CargarAsync();

        Assert.Equal(["INVENTARIO", "TAREAS"], catalogos.Grupos.Where(g => g.EsVisible).Select(g => g.Titulo));
        Assert.Equal(ClaveCatalogo.Zonas, catalogos.CatalogoActual!.Clave);
        Assert.Equal(5, catalogos.CatalogoActual.Filas.Count);
        Assert.Equal("Catalogos", shell.SeccionActiva);
    }

    [Fact]
    public async Task Estados_CargandoSinConexionSinPermisoVacioYSinResultados()
    {
        var cargando = (CatalogosViewModel)DatosCatalogos.Cargando().CurrentContent!;
        _ = cargando.CargarAsync();
        Assert.True(cargando.CatalogoActual!.Cargando);

        var sinConexion = (CatalogosViewModel)DatosCatalogos.SinConexion().CurrentContent!;
        await sinConexion.CargarAsync();
        Assert.True(sinConexion.CatalogoActual!.SinConexion);

        var sinPermiso = (CatalogosViewModel)DatosCatalogos.SinPermiso().CurrentContent!;
        await sinPermiso.CargarAsync();
        Assert.True(sinPermiso.CatalogoActual!.SinPermiso);
        Assert.Equal("No tenés permiso para ver las fuentes de financiamiento.", sinPermiso.CatalogoActual.MensajeSinPermiso);

        var vacio = (CatalogosViewModel)DatosCatalogos.Vacio().CurrentContent!;
        await vacio.CargarAsync();
        Assert.Equal(ClaveCatalogo.UnidadesMedida, vacio.CatalogoActual!.Clave);
        Assert.Empty(vacio.CatalogoActual.Filas);
        Assert.Equal("Nueva unidad", vacio.CatalogoActual.TextoNuevo);

        var sinResultados = (CatalogosViewModel)DatosCatalogos.SinResultados().CurrentContent!;
        await sinResultados.CargarAsync();
        Assert.True(sinResultados.CatalogoActual!.SinCoincidencias);
        Assert.Equal("zzz", sinResultados.CatalogoActual.Busqueda);
    }

    [Fact]
    public void Formularios_TitulosYTextosDelMockup()
    {
        var nueva = (CategoriaFormViewModel)DatosCatalogos.CategoriaNueva().CurrentContent!;
        Assert.Equal("Nueva categoría", nueva.Titulo);

        var error = (CategoriaFormViewModel)DatosCatalogos.CategoriaError().CurrentContent!;
        Assert.Equal("Ya existe una categoría con ese nombre.", error.MensajeError);
        Assert.Equal("Electricidad", error.Nombre);

        var proveedor = (ProveedorFormViewModel)DatosCatalogos.ProveedorEditar().CurrentContent!;
        Assert.Equal("Editar proveedor", proveedor.Titulo);
        Assert.Equal("ventas@barraca.uy", proveedor.Email);

        var rubro = (RubroGastoFormViewModel)DatosCatalogos.RubroNuevo().CurrentContent!;
        Assert.Equal("Nuevo rubro de gasto", rubro.Titulo);

        var linea = (LineaPoaFormViewModel)DatosCatalogos.LineaPoaEditar().CurrentContent!;
        Assert.Equal("Editar línea POA", linea.Titulo);
    }

    [Fact]
    public async Task LineaPoaEditar_DosAsignacionesConMontoEsUy()
    {
        var linea = (LineaPoaFormViewModel)DatosCatalogos.LineaPoaEditar().CurrentContent!;
        await linea.InicializarAsync();

        Assert.Equal(2, linea.Asignaciones.Count);
        Assert.Equal(("Rentas Generales", "1.250.000"), (linea.Asignaciones[0].FuenteSeleccionada!.Nombre, linea.Asignaciones[0].MontoTexto));
        Assert.Equal(("FIGM Literal B", "380.000"), (linea.Asignaciones[1].FuenteSeleccionada!.Nombre, linea.Asignaciones[1].MontoTexto));
    }

    [Fact]
    public void Usuarios_SinSeleccion_OperadorAdminInactivo()
    {
        var sin = (UsuariosAdminViewModel)DatosUsuarios.SinSeleccion().CurrentContent!;
        Assert.Null(sin.UsuarioSeleccionado);

        var operador = (UsuariosAdminViewModel)DatosUsuarios.OperadorSeleccionado().CurrentContent!;
        Assert.Equal("jrodriguez", operador.UsuarioSeleccionado!.NombreUsuario);
        Assert.True(operador.PanelPermisos.PuedeEditar);
        Assert.Equal(4, operador.PanelPermisos.Grupos.SelectMany(g => g.Items).Count(i => i.Seleccionado));
        Assert.Contains(operador.PanelPermisos.Grupos.SelectMany(g => g.Items), i => i.Advertencia is not null);

        var admin = (UsuariosAdminViewModel)DatosUsuarios.AdminSeleccionado().CurrentContent!;
        Assert.True(admin.EsAdminSeleccionado);
        Assert.False(admin.PanelPermisos.PuedeEditar);

        var inactivo = (UsuariosAdminViewModel)DatosUsuarios.InactivoSeleccionado().CurrentContent!;
        Assert.True(inactivo.PanelPermisos.MostrarAvisoSoloLectura);
    }

    [Fact]
    public void Usuarios_AltaConError_MensajeDeLaRegla()
    {
        var vm = (UsuariosAdminViewModel)DatosUsuarios.AltaConError().CurrentContent!;

        Assert.Equal("Ya existe un usuario con ese nombre.", vm.MensajeError);
        Assert.Equal("jrodriguez", vm.NuevoNombreUsuario);
    }

    [Fact]
    public async Task Mantenimiento_ConCorridas_TresCorridasLogsYAlertas()
    {
        var vm = (MantenimientoViewModel)DatosMantenimiento.ConCorridas().CurrentContent!;
        await vm.CargarAsync();

        Assert.Equal(3, vm.Corridas.Count);
        Assert.True(vm.Corridas[1].EsFallo);
        Assert.True(vm.Corridas[2].EsNotaInformativa);
        Assert.StartsWith("12 archivo(s), 3", vm.TextoResumenLogs);
        Assert.EndsWith("MB, del 25/09/2026 al 06/10/2026.", vm.TextoResumenLogs);
        Assert.Equal("https://hooks.municipio.uy/alertas/stockapp", vm.UrlWebhook);
        Assert.True(vm.AlertasHabilitadas);
    }

    [Fact]
    public async Task Mantenimiento_Vacio_YConErrores()
    {
        var vacio = (MantenimientoViewModel)DatosMantenimiento.Vacio().CurrentContent!;
        await vacio.CargarAsync();
        Assert.True(vacio.MostrarListaVacia);
        Assert.Equal("No hay archivos de log todavía.", vacio.TextoResumenLogs);

        var errores = (MantenimientoViewModel)DatosMantenimiento.ConErrores().CurrentContent!;
        await errores.CargarAsync();
        Assert.True(errores.ErrorAlCargar);
        Assert.False(errores.MostrarListaVacia);
        Assert.True(errores.ErrorAlCargarAlertas);
        Assert.False(errores.PuedeGuardarAlertas);
    }
}
