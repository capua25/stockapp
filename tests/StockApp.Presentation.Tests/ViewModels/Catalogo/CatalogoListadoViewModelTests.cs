using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Catalogo;

public class CatalogoListadoViewModelTests
{
    private static INavigationService Nav() => new Mock<INavigationService>().Object;

    private static IConfirmacionService Confirma(bool respuesta = true)
    {
        var m = new Mock<IConfirmacionService>();
        m.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(respuesta);
        m.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        return m.Object;
    }

    private static (CategoriaListViewModel vm, Mock<ICategoriaService> svc) CrearCategorias(params string[] nombres)
    {
        var svc = new Mock<ICategoriaService>();
        var lista = nombres.Select((n, i) => new Categoria { Id = i + 1, Nombre = n }).ToList();
        svc.Setup(s => s.ListarTodasAsync()).ReturnsAsync(lista);
        svc.Setup(s => s.BajaLogicaAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
        return (new CategoriaListViewModel(svc.Object, Nav(), Confirma()), svc);
    }

    public static IEnumerable<object[]> LasDiezClaves()
    {
        yield return new object[] { ClaveCatalogo.Categorias, "Categorías", "Nueva categoría", "Inactiva", "No tenés permiso para ver las categorías.", Array.Empty<string>() };
        yield return new object[] { ClaveCatalogo.Proveedores, "Proveedores", "Nuevo proveedor", "Inactivo", "No tenés permiso para ver los proveedores.", new[] { "TELÉFONO", "EMAIL", "DIRECCIÓN", "NOTAS" } };
        yield return new object[] { ClaveCatalogo.UnidadesMedida, "Unidades de medida", "Nueva unidad", "Inactiva", "No tenés permiso para ver las unidades de medida.", new[] { "ABREVIATURA" } };
        yield return new object[] { ClaveCatalogo.FuentesFinanciamiento, "Fuentes de financiamiento", "Nueva fuente", "Inactiva", "No tenés permiso para ver las fuentes de financiamiento.", Array.Empty<string>() };
        yield return new object[] { ClaveCatalogo.RubrosGasto, "Rubros de gasto", "Nuevo rubro", "Inactivo", "No tenés permiso para ver los rubros de gasto.", new[] { "CÓDIGO" } };
        yield return new object[] { ClaveCatalogo.LineasPoa, "Líneas POA", "Nueva línea POA", "Inactiva", "No tenés permiso para ver las líneas POA.", new[] { "PROGRAMA", "EJERCICIO" } };
        yield return new object[] { ClaveCatalogo.Zonas, "Zonas", "Nueva zona", "Inactiva", "No tenés permiso para ver las zonas.", Array.Empty<string>() };
        yield return new object[] { ClaveCatalogo.Dimensiones, "Dimensiones", "Nueva dimensión", "Inactiva", "No tenés permiso para ver las dimensiones.", Array.Empty<string>() };
        yield return new object[] { ClaveCatalogo.Organismos, "Organismos responsables", "Nuevo organismo", "Inactivo", "No tenés permiso para ver los organismos responsables.", Array.Empty<string>() };
        yield return new object[] { ClaveCatalogo.Origenes, "Orígenes de financiamiento", "Nuevo origen", "Inactivo", "No tenés permiso para ver los orígenes de financiamiento.", Array.Empty<string>() };
    }

    private static CatalogoListadoViewModel CrearDeClave(ClaveCatalogo clave)
    {
        var nav = Nav();
        var conf = Confirma();
        return clave switch
        {
            ClaveCatalogo.Categorias => new CategoriaListViewModel(new Mock<ICategoriaService>().Object, nav, conf),
            ClaveCatalogo.Proveedores => new ProveedorListViewModel(new Mock<IProveedorService>().Object, nav, conf),
            ClaveCatalogo.UnidadesMedida => new UnidadMedidaListViewModel(new Mock<IUnidadMedidaService>().Object, nav, conf),
            ClaveCatalogo.FuentesFinanciamiento => new FuenteFinanciamientoListViewModel(new Mock<IFuenteFinanciamientoService>().Object, nav, conf),
            ClaveCatalogo.RubrosGasto => new RubroGastoListViewModel(new Mock<IRubroGastoService>().Object, nav, conf),
            ClaveCatalogo.LineasPoa => new LineaPoaListViewModel(new Mock<ILineaPoaService>().Object, nav, conf),
            ClaveCatalogo.Zonas => new ZonaListViewModel(new Mock<IZonaService>().Object, nav, conf),
            ClaveCatalogo.Dimensiones => new DimensionTematicaListViewModel(new Mock<IDimensionTematicaService>().Object, nav, conf),
            ClaveCatalogo.Organismos => new OrganismoResponsableListViewModel(new Mock<IOrganismoResponsableService>().Object, nav, conf),
            ClaveCatalogo.Origenes => new OrigenFinanciamientoListViewModel(new Mock<IOrigenFinanciamientoService>().Object, nav, conf),
            _ => throw new ArgumentOutOfRangeException(nameof(clave)),
        };
    }

    [Theory]
    [MemberData(nameof(LasDiezClaves))]
    public void Textos_SonLosAprobados(ClaveCatalogo clave, string titulo, string nuevo, string inactivo, string sinPermiso, string[] columnas)
    {
        var vm = CrearDeClave(clave);

        Assert.Equal(clave, vm.Clave);
        Assert.Equal(titulo, vm.Titulo);
        Assert.Equal(nuevo, vm.TextoNuevo);
        Assert.Equal(inactivo, vm.TextoInactivo);
        Assert.Equal(sinPermiso, vm.TextoSinPermiso);
        Assert.Equal(columnas, vm.Columnas.Select(c => c.Encabezado));
    }

    [Fact]
    public async Task Cargar_ArmaUnaFilaPorItemConLosValoresDeLasColumnas()
    {
        var proveedor = new Proveedor
        {
            Id = 1, Nombre = "Barraca Carmelo", Telefono = "4542 2210", Email = "ventas@barraca.uy",
            Direccion = "18 de Julio 455", Notas = null, Activo = false,
        };
        var svc = new Mock<IProveedorService>();
        svc.Setup(s => s.ListarTodosAsync()).ReturnsAsync(new List<Proveedor> { proveedor });
        var vm = new ProveedorListViewModel(svc.Object, Nav(), Confirma());

        await vm.CargarAsync();

        var fila = Assert.Single(vm.Filas);
        Assert.Equal("Barraca Carmelo", fila.Valor0);
        Assert.Equal("4542 2210", fila.Valor1);
        Assert.Equal("ventas@barraca.uy", fila.Valor2);
        Assert.Equal("18 de Julio 455", fila.Valor3);
        Assert.Null(fila.Valor4);
        Assert.False(fila.Activo);
        Assert.Same(proveedor, fila.Entidad);
    }

    [Fact]
    public async Task Cargar_RubroYLineaPoa_PonenElNombrePrimeroYLosNumerosSinFormato()
    {
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(s => s.ListarTodosAsync()).ReturnsAsync(new List<RubroGasto> { new() { Id = 1, Codigo = 1234, Nombre = "Combustibles" } });
        var rubro = new RubroGastoListViewModel(rubros.Object, Nav(), Confirma());
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(s => s.ListarTodasAsync()).ReturnsAsync(new List<LineaPoa> { new() { Id = 1, Nombre = "Caminería", Programa = "Obras", Ejercicio = 2026 } });
        var linea = new LineaPoaListViewModel(lineas.Object, Nav(), Confirma());

        await rubro.CargarAsync();
        await linea.CargarAsync();

        Assert.Equal(("Combustibles", "1234"), (rubro.Filas[0].Valor0, rubro.Filas[0].Valor1));
        Assert.Equal(("Caminería", "Obras", "2026"), (linea.Filas[0].Valor0, linea.Filas[0].Valor1, linea.Filas[0].Valor2));
    }

    [Fact]
    public async Task Busqueda_FiltraSinMayusculasNiTildes_YCuentaLasVisibles()
    {
        var (vm, _) = CrearCategorias("Electricidad", "Plomería", "Pinturería");
        await vm.CargarAsync();

        vm.Busqueda = "PLOMERIA";

        Assert.Equal(1, vm.CantidadVisible);
        var visibles = vm.FilasView.Cast<FilaCatalogo>().ToList();
        Assert.Equal("Plomería", Assert.Single(visibles).Valor0);

        vm.Busqueda = "";

        Assert.Equal(3, vm.CantidadVisible);
    }

    [Fact]
    public async Task Busqueda_BuscaEnTodasLasColumnas()
    {
        var svc = new Mock<IProveedorService>();
        svc.Setup(s => s.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>
        {
            new() { Id = 1, Nombre = "Barraca Carmelo", Email = "compras@ferreteria.uy" },
            new() { Id = 2, Nombre = "Pinturas del Sur" },
        });
        var vm = new ProveedorListViewModel(svc.Object, Nav(), Confirma());
        await vm.CargarAsync();

        vm.Busqueda = "ferreteria";

        Assert.Equal(1, vm.CantidadVisible);
    }

    [Fact]
    public async Task Busqueda_QueOcultaLaFilaSeleccionada_LimpiaLaSeleccionYDeshabilitaAcciones()
    {
        var (vm, _) = CrearCategorias("Electricidad", "Plomería");
        await vm.CargarAsync();
        vm.ItemSeleccionado = vm.Items[1];
        Assert.True(vm.EditarCommand.CanExecute(null));

        vm.Busqueda = "elec";

        Assert.Null(vm.ItemSeleccionado);
        Assert.Null(vm.FilaSeleccionada);
        Assert.False(vm.EditarCommand.CanExecute(null));
        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task FilaSeleccionada_EItemSeleccionado_SeSincronizan()
    {
        var (vm, _) = CrearCategorias("Electricidad", "Plomería");
        await vm.CargarAsync();

        vm.FilaSeleccionada = vm.Filas[1];
        Assert.Same(vm.Items[1], vm.ItemSeleccionado);

        vm.ItemSeleccionado = vm.Items[0];
        Assert.Same(vm.Filas[0], vm.FilaSeleccionada);
    }

    [Fact]
    public async Task SinCoincidencias_SoloConFilasYNingunaVisible()
    {
        var (vacio, _) = CrearCategorias();
        await vacio.CargarAsync();
        Assert.False(vacio.SinCoincidencias);

        var (vm, _) = CrearCategorias("Electricidad");
        await vm.CargarAsync();
        vm.Busqueda = "zzz";
        Assert.True(vm.SinCoincidencias);

        vm.Busqueda = "";
        Assert.False(vm.SinCoincidencias);
    }

    [Fact]
    public async Task Baja_RecargaYConservaLaBusqueda()
    {
        var svc = new Mock<ICategoriaService>();
        var antes = new List<Categoria>
        {
            new() { Id = 1, Nombre = "Electricidad" },
            new() { Id = 2, Nombre = "Pinturería" },
        };
        var despues = new List<Categoria>
        {
            new() { Id = 1, Nombre = "Electricidad" },
            new() { Id = 2, Nombre = "Pinturería", Activo = false },
        };
        svc.SetupSequence(s => s.ListarTodasAsync()).ReturnsAsync(antes).ReturnsAsync(despues);
        svc.Setup(s => s.BajaLogicaAsync(2)).Returns(Task.CompletedTask);
        var vm = new CategoriaListViewModel(svc.Object, Nav(), Confirma());
        await vm.CargarAsync();
        vm.Busqueda = "pin";
        vm.ItemSeleccionado = vm.Items[1];

        await vm.BajaCommand.ExecuteAsync(null);

        Assert.Equal("pin", vm.Busqueda);
        Assert.Equal(1, vm.CantidadVisible);
        var visible = Assert.Single(vm.FilasView.Cast<FilaCatalogo>());
        Assert.Equal("Pinturería", visible.Valor0);
        Assert.False(visible.Activo);
    }

    [Fact]
    public async Task Cargar_Un403_MarcaSinPermisoConSuMensaje_YVaciaFilas()
    {
        var svc = new Mock<ICategoriaService>();
        svc.SetupSequence(s => s.ListarTodasAsync())
            .ReturnsAsync(new List<Categoria> { new() { Id = 1, Nombre = "Electricidad" } })
            .ThrowsAsync(new UnauthorizedAccessException());
        var vm = new CategoriaListViewModel(svc.Object, Nav(), Confirma());
        await vm.CargarAsync();
        Assert.Single(vm.Filas);

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal("No tenés permiso para ver las categorías.", vm.MensajeSinPermiso);
        Assert.Empty(vm.Filas);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task Cargar_SinServidor_MarcaSinConexion_YReintentarRecarga()
    {
        var svc = new Mock<ICategoriaService>();
        svc.SetupSequence(s => s.ListarTodasAsync())
            .ReturnsAsync(new List<Categoria> { new() { Id = 1, Nombre = "Electricidad" } })
            .ThrowsAsync(new ServidorNoDisponibleException())
            .ReturnsAsync(new List<Categoria> { new() { Id = 1, Nombre = "Electricidad" } });
        var vm = new CategoriaListViewModel(svc.Object, Nav(), Confirma());
        await vm.CargarAsync();

        await vm.CargarAsync();

        Assert.True(vm.SinConexion);
        Assert.Empty(vm.Filas);

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(vm.SinConexion);
        Assert.Single(vm.Filas);
    }

    [Fact]
    public async Task DosCargas_LaQueTerminaTarde_NoPisaALaUltima()
    {
        var primera = new TaskCompletionSource<IReadOnlyList<Categoria>>();
        var segunda = new TaskCompletionSource<IReadOnlyList<Categoria>>();
        var svc = new Mock<ICategoriaService>();
        svc.SetupSequence(s => s.ListarTodasAsync()).Returns(primera.Task).Returns(segunda.Task);
        var vm = new CategoriaListViewModel(svc.Object, Nav(), Confirma());

        var cargaA = vm.CargarAsync();
        var cargaB = vm.CargarAsync();
        segunda.SetResult(new List<Categoria> { new() { Id = 2, Nombre = "B" } });
        await cargaB;
        primera.SetResult(new List<Categoria> { new() { Id = 1, Nombre = "A" } });
        await cargaA;

        Assert.Equal("B", Assert.Single(vm.Filas).Valor0);
    }

    [Fact]
    public async Task Frases_DeLosEstados_YPieConSingularYPlural()
    {
        var (vm, _) = CrearCategorias("Electricidad", "Plomería");
        Assert.Equal("Cargando categorías…", vm.TextoCargando);
        Assert.Equal("Todavía no hay categorías", vm.TituloSinDatos);
        Assert.Equal("Tocá Nueva categoría para cargar el primero.", vm.MensajeSinDatos);

        await vm.CargarAsync();
        Assert.Equal("2 categorías", vm.TextoPie);
        vm.Busqueda = "elec";
        Assert.Equal("1 categoría", vm.TextoPie);

        var lineas = CrearDeClave(ClaveCatalogo.LineasPoa);
        Assert.Equal("Cargando líneas POA…", lineas.TextoCargando);
        Assert.Equal("Todavía no hay unidades de medida", CrearDeClave(ClaveCatalogo.UnidadesMedida).TituloSinDatos);
    }

    [Fact]
    public void Rubros_MuestraElCodigoAntesQueElNombre_LosDemasNo()
    {
        Assert.Equal(1, CrearDeClave(ClaveCatalogo.RubrosGasto).PosicionNombre);
        foreach (var clave in Enum.GetValues<ClaveCatalogo>().Where(c => c != ClaveCatalogo.RubrosGasto))
            Assert.Equal(0, CrearDeClave(clave).PosicionNombre);
    }

    [Fact]
    public async Task Orden_DelCodigoYDelEjercicio_EsNumericoYElRestoOrdenaPorTexto()
    {
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(s => s.ListarTodosAsync()).ReturnsAsync(new List<RubroGasto> { new() { Id = 1, Codigo = 10, Nombre = "A" } });
        var rubro = new RubroGastoListViewModel(rubros.Object, Nav(), Confirma());
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(s => s.ListarTodasAsync()).ReturnsAsync(new List<LineaPoa> { new() { Id = 1, Nombre = "L", Programa = "Obras", Ejercicio = 2026 } });
        var linea = new LineaPoaListViewModel(lineas.Object, Nav(), Confirma());
        var prov = new Mock<IProveedorService>();
        prov.Setup(s => s.ListarTodosAsync()).ReturnsAsync(new List<Proveedor> { new() { Id = 1, Nombre = "P", Telefono = "4542 2210" } });
        var proveedor = new ProveedorListViewModel(prov.Object, Nav(), Confirma());

        await rubro.CargarAsync();
        await linea.CargarAsync();
        await proveedor.CargarAsync();

        Assert.Equal(10, Assert.IsType<int>(rubro.Filas[0].Orden1));
        Assert.Equal("Obras", linea.Filas[0].Orden1);
        Assert.Equal(2026, Assert.IsType<int>(linea.Filas[0].Orden2));
        Assert.Equal("4542 2210", proveedor.Filas[0].Orden1);
    }
}
