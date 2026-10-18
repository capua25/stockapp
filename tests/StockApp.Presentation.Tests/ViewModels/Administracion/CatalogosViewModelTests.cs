using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Administracion;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Administracion;

public class CatalogosViewModelTests
{
    /// <summary>Servicios de catálogo con una llamada contada por cada uno, para saber cuál se consultó.</summary>
    private sealed class Servicios
    {
        public Mock<ICategoriaService> Categorias { get; } = Vacio<ICategoriaService, Categoria>(s => s.ListarTodasAsync());
        public Mock<IProveedorService> Proveedores { get; } = Vacio<IProveedorService, Proveedor>(s => s.ListarTodosAsync());
        public Mock<IUnidadMedidaService> Unidades { get; } = Vacio<IUnidadMedidaService, UnidadMedida>(s => s.ListarTodasAsync());
        public Mock<IFuenteFinanciamientoService> Fuentes { get; } = Vacio<IFuenteFinanciamientoService, FuenteFinanciamiento>(s => s.ListarTodasAsync());
        public Mock<IRubroGastoService> Rubros { get; } = Vacio<IRubroGastoService, RubroGasto>(s => s.ListarTodosAsync());
        public Mock<ILineaPoaService> Lineas { get; } = Vacio<ILineaPoaService, LineaPoa>(s => s.ListarTodasAsync());
        public Mock<IZonaService> Zonas { get; } = Vacio<IZonaService, Zona>(s => s.ListarTodasAsync());
        public Mock<IDimensionTematicaService> Dimensiones { get; } = Vacio<IDimensionTematicaService, DimensionTematica>(s => s.ListarTodasAsync());
        public Mock<IOrganismoResponsableService> Organismos { get; } = Vacio<IOrganismoResponsableService, OrganismoResponsable>(s => s.ListarTodasAsync());
        public Mock<IOrigenFinanciamientoService> Origenes { get; } = Vacio<IOrigenFinanciamientoService, OrigenFinanciamiento>(s => s.ListarTodasAsync());

        private static Mock<TSvc> Vacio<TSvc, TEntidad>(System.Linq.Expressions.Expression<Func<TSvc, Task<IReadOnlyList<TEntidad>>>> listar)
            where TSvc : class
        {
            var m = new Mock<TSvc>();
            m.Setup(listar).ReturnsAsync(new List<TEntidad>());
            return m;
        }
    }

    private static Mock<ICurrentSession> Sesion(RolUsuario rol, params string[] permisos)
    {
        var s = new Mock<ICurrentSession>();
        s.Setup(x => x.RolActual).Returns(rol);
        var conjunto = new HashSet<string>(permisos);
        s.Setup(x => x.PermisosActuales).Returns(conjunto);
        return s;
    }

    private static (CatalogosViewModel vm, Servicios svc) Crear(Mock<ICurrentSession> sesion)
    {
        var svc = new Servicios();
        var nav = new Mock<INavigationService>().Object;
        var conf = new Mock<IConfirmacionService>().Object;
        var vm = new CatalogosViewModel(
            sesion.Object,
            new CategoriaListViewModel(svc.Categorias.Object, nav, conf),
            new ProveedorListViewModel(svc.Proveedores.Object, nav, conf),
            new UnidadMedidaListViewModel(svc.Unidades.Object, nav, conf),
            new FuenteFinanciamientoListViewModel(svc.Fuentes.Object, nav, conf),
            new RubroGastoListViewModel(svc.Rubros.Object, nav, conf),
            new LineaPoaListViewModel(svc.Lineas.Object, nav, conf),
            new ZonaListViewModel(svc.Zonas.Object, nav, conf),
            new DimensionTematicaListViewModel(svc.Dimensiones.Object, nav, conf),
            new OrganismoResponsableListViewModel(svc.Organismos.Object, nav, conf),
            new OrigenFinanciamientoListViewModel(svc.Origenes.Object, nav, conf));
        return (vm, svc);
    }

    [Fact]
    public void Admin_VeLosTresGruposConLosDiezCatalogosEnElOrdenDeLaSpec()
    {
        var (vm, _) = Crear(Sesion(RolUsuario.Admin));

        Assert.Equal(new[] { "INVENTARIO", "FINANZAS", "TAREAS" }, vm.Grupos.Select(g => g.Titulo));
        Assert.Equal(new[] { "Categorías", "Proveedores", "Unidades de medida" }, vm.Grupos[0].Entradas.Select(e => e.Titulo));
        Assert.Equal(new[] { "Fuentes de financiamiento", "Rubros de gasto", "Líneas POA" }, vm.Grupos[1].Entradas.Select(e => e.Titulo));
        Assert.Equal(new[] { "Zonas", "Dimensiones", "Organismos responsables", "Orígenes de financiamiento" }, vm.Grupos[2].Entradas.Select(e => e.Titulo));
        Assert.All(vm.Grupos, g => Assert.True(g.EsVisible));
    }

    [Fact]
    public void OperadorConTablasMaestras_VeInventarioYTareas_NoFinanzas()
    {
        var (vm, _) = Crear(Sesion(RolUsuario.Operador, Permisos.GestionarTablasMaestras));

        Assert.Equal(new[] { "INVENTARIO", "TAREAS" }, vm.Grupos.Where(g => g.EsVisible).Select(g => g.Titulo));
    }

    [Fact]
    public void OperadorConMaestrosFinanzas_SoloVeFinanzas()
    {
        var (vm, _) = Crear(Sesion(RolUsuario.Operador, Permisos.GestionarMaestrosFinanzas));

        Assert.Equal(new[] { "FINANZAS" }, vm.Grupos.Where(g => g.EsVisible).Select(g => g.Titulo));
    }

    [Fact]
    public async Task Cargar_SinPendiente_SeleccionaElPrimerVisibleYLoCarga()
    {
        var (vm, svc) = Crear(Sesion(RolUsuario.Operador, Permisos.GestionarMaestrosFinanzas));

        await vm.CargarAsync();

        Assert.Equal(ClaveCatalogo.FuentesFinanciamiento, vm.CatalogoActual!.Clave);
        Assert.True(vm.Grupos[1].Entradas[0].EstaSeleccionada);
        svc.Fuentes.Verify(s => s.ListarTodasAsync(), Times.Once);
    }

    [Fact]
    public async Task Seleccionar_YDespuesCargar_AbreEseCatalogo()
    {
        var (vm, svc) = Crear(Sesion(RolUsuario.Admin));

        vm.Seleccionar(ClaveCatalogo.UnidadesMedida);
        await vm.CargarAsync();

        Assert.Equal(ClaveCatalogo.UnidadesMedida, vm.CatalogoActual!.Clave);
        svc.Unidades.Verify(s => s.ListarTodasAsync(), Times.Once);
        svc.Categorias.Verify(s => s.ListarTodasAsync(), Times.Never);
    }

    [Fact]
    public async Task Seleccionar_UnCatalogoDeUnGrupoSinPermiso_AbreElPrimerVisible()
    {
        var (vm, _) = Crear(Sesion(RolUsuario.Operador, Permisos.GestionarTablasMaestras));

        vm.Seleccionar(ClaveCatalogo.LineasPoa);
        await vm.CargarAsync();

        Assert.Equal(ClaveCatalogo.Categorias, vm.CatalogoActual!.Clave);
    }

    [Fact]
    public async Task SeleccionarCommand_CambiaDeCatalogo_LimpiaLaBusquedaYRecarga()
    {
        var (vm, svc) = Crear(Sesion(RolUsuario.Admin));
        await vm.CargarAsync();
        vm.CatalogoActual!.Busqueda = "x";
        var zonas = vm.Grupos[2].Entradas[0];
        zonas.Listado.Busqueda = "residuo";

        await vm.SeleccionarCommand.ExecuteAsync(zonas);

        Assert.False(vm.Grupos[0].Entradas[0].EstaSeleccionada);
        Assert.True(zonas.EstaSeleccionada);
        Assert.Equal("", zonas.Listado.Busqueda);
        svc.Zonas.Verify(s => s.ListarTodasAsync(), Times.Once);
    }

    [Fact]
    public async Task RevocarElGrupoDelCatalogoAbierto_SeleccionaElPrimerVisible()
    {
        var sesion = Sesion(RolUsuario.Operador, Permisos.GestionarTablasMaestras, Permisos.GestionarMaestrosFinanzas);
        var (vm, _) = Crear(sesion);
        vm.Seleccionar(ClaveCatalogo.LineasPoa);
        await vm.CargarAsync();
        Assert.Equal(ClaveCatalogo.LineasPoa, vm.CatalogoActual!.Clave);

        sesion.Setup(x => x.PermisosActuales).Returns(new HashSet<string> { Permisos.GestionarTablasMaestras });
        vm.NotificarPermisosActualizados();

        Assert.False(vm.Grupos[1].EsVisible);
        Assert.Equal(ClaveCatalogo.Categorias, vm.CatalogoActual!.Clave);
    }

    [Fact]
    public async Task RevocarTodosLosGrupos_MarcaSinPermiso()
    {
        var sesion = Sesion(RolUsuario.Operador, Permisos.GestionarTablasMaestras, Permisos.GestionarMaestrosFinanzas);
        var (vm, _) = Crear(sesion);
        await vm.CargarAsync();

        sesion.Setup(x => x.PermisosActuales).Returns(new HashSet<string>());
        vm.NotificarPermisosActualizados();

        Assert.True(vm.SinPermiso);
        Assert.Equal(CatalogosViewModel.TextoSinPermiso, vm.MensajeSinPermiso);
        Assert.Null(vm.CatalogoActual);
    }

    [Fact]
    public async Task NotificarPermisosActualizados_ReenviaAlListadoAbierto()
    {
        var (vm, _) = Crear(Sesion(RolUsuario.Admin));
        await vm.CargarAsync();
        var avisos = new List<string?>();
        vm.CatalogoActual!.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.NotificarPermisosActualizados();

        Assert.Contains(string.Empty, avisos);
    }

    // ── los formularios vuelven a Catálogos con su catálogo ───────────────────

    /// <summary>Navegación que ejecuta el inicializador sobre el CatalogosViewModel real, como lo hace el shell.</summary>
    private sealed class NavegacionAlCatalogos : INavigationService
    {
        private readonly CatalogosViewModel _destino;
        public NavegacionAlCatalogos(CatalogosViewModel destino) => _destino = destino;
        public Type? Ultimo { get; private set; }
        public ViewModelBase? Actual => null;
        public event Action? Cambiado { add { } remove { } }
        public void Navegar<TVm>() where TVm : ViewModelBase => Ultimo = typeof(TVm);
        public void Navegar<TVm>(Action<TVm> inicializar) where TVm : ViewModelBase
        {
            Ultimo = typeof(TVm);
            if (_destino is TVm destino)
                inicializar(destino);
        }
    }

    private sealed record Formulario(ClaveCatalogo Clave, Func<INavigationService, (IRelayCommand Cancelar, IAsyncRelayCommand Guardar)> Crear);

    public static IEnumerable<object[]> LosDiezFormularios()
    {
        Formulario[] todos =
        [
            new(ClaveCatalogo.Categorias, nav =>
            {
                var f = new CategoriaFormViewModel(Mock.Of<ICategoriaService>(), nav) { Nombre = "X" };
                return (f.CancelarCommand, f.GuardarCommand);
            }),
            new(ClaveCatalogo.Proveedores, nav =>
            {
                var f = new ProveedorFormViewModel(Mock.Of<IProveedorService>(), nav) { Nombre = "X" };
                return (f.CancelarCommand, f.GuardarCommand);
            }),
            new(ClaveCatalogo.UnidadesMedida, nav =>
            {
                var f = new UnidadMedidaFormViewModel(Mock.Of<IUnidadMedidaService>(), nav) { Nombre = "X", Abreviatura = "x" };
                return (f.CancelarCommand, f.GuardarCommand);
            }),
            new(ClaveCatalogo.FuentesFinanciamiento, nav =>
            {
                var f = new FuenteFinanciamientoFormViewModel(Mock.Of<IFuenteFinanciamientoService>(), nav) { Nombre = "X" };
                return (f.CancelarCommand, f.GuardarCommand);
            }),
            new(ClaveCatalogo.RubrosGasto, nav =>
            {
                var f = new RubroGastoFormViewModel(Mock.Of<IRubroGastoService>(), nav) { Nombre = "X", CodigoTexto = "10" };
                return (f.CancelarCommand, f.GuardarCommand);
            }),
            new(ClaveCatalogo.LineasPoa, nav =>
            {
                var f = new LineaPoaFormViewModel(Mock.Of<ILineaPoaService>(), Mock.Of<IFuenteFinanciamientoService>(), nav)
                    { Nombre = "X", Programa = "P", EjercicioTexto = "2026" };
                return (f.CancelarCommand, f.GuardarCommand);
            }),
            new(ClaveCatalogo.Zonas, nav =>
            {
                var f = new ZonaFormViewModel(Mock.Of<IZonaService>(), nav) { Nombre = "X" };
                return (f.CancelarCommand, f.GuardarCommand);
            }),
            new(ClaveCatalogo.Dimensiones, nav =>
            {
                var f = new DimensionTematicaFormViewModel(Mock.Of<IDimensionTematicaService>(), nav) { Nombre = "X" };
                return (f.CancelarCommand, f.GuardarCommand);
            }),
            new(ClaveCatalogo.Organismos, nav =>
            {
                var f = new OrganismoResponsableFormViewModel(Mock.Of<IOrganismoResponsableService>(), nav) { Nombre = "X" };
                return (f.CancelarCommand, f.GuardarCommand);
            }),
            new(ClaveCatalogo.Origenes, nav =>
            {
                var f = new OrigenFinanciamientoFormViewModel(Mock.Of<IOrigenFinanciamientoService>(), nav) { Nombre = "X" };
                return (f.CancelarCommand, f.GuardarCommand);
            }),
        ];
        return todos.SelectMany(f => new[] { new object[] { f, false }, new object[] { f, true } });
    }

    [Theory]
    [MemberData(nameof(LosDiezFormularios))]
    public async Task GuardarYCancelar_VuelvenACatalogosConSuClave(object formularioObj, bool guardar)
    {
        var formulario = (Formulario)formularioObj;
        var (catalogos, _) = Crear(Sesion(RolUsuario.Admin));
        var nav = new NavegacionAlCatalogos(catalogos);
        var (cancelar, guardarCmd) = formulario.Crear(nav);

        if (guardar)
            await guardarCmd.ExecuteAsync(null);
        else
            cancelar.Execute(null);
        await catalogos.CargarAsync();

        Assert.Equal(typeof(CatalogosViewModel), nav.Ultimo);
        Assert.Equal(formulario.Clave, catalogos.CatalogoActual!.Clave);
    }
}
