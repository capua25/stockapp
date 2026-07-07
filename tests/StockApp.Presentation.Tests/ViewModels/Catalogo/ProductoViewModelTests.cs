using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Catalogo;

public class ProductoListViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static ProductoDto CrearProductoDto(
        int id, string codigo, string nombre, bool activo = true,
        int unidadMedidaId = 1, decimal precioCosto = 0m, decimal precioVenta = 0m)
        => new ProductoDto(
            Id: id, Codigo: codigo, CodigoBarras: null, Nombre: nombre, Descripcion: null,
            CategoriaId: null, CategoriaNombre: null, ProveedorId: null, UnidadMedidaId: unidadMedidaId,
            UnidadMedidaNombre: "Unidad", PrecioCosto: precioCosto, PrecioVenta: precioVenta,
            StockActual: 0m, StockMinimo: 0m, Activo: activo, FechaAlta: default);

    private static (ProductoListViewModel vm, Mock<IProductoService> svcMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<ProductoDto>? productos = null)
    {
        var svcMock = new Mock<IProductoService>();
        svcMock
            .Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(productos ?? new List<ProductoDto>());
        svcMock
            .Setup(s => s.BuscarPorTextoAsync(It.IsAny<string?>()))
            .ReturnsAsync(productos ?? new List<ProductoDto>());
        svcMock
            .Setup(s => s.BajaLogicaAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();

        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new ProductoListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock, confirmMock);
    }

    // ── D3.1 tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CargarAsync_LlamaServicioYPopulaItems()
    {
        var productos = new List<ProductoDto>
        {
            CrearProductoDto(1, "P001", "Producto Uno"),
            CrearProductoDto(2, "P002", "Producto Dos")
        };
        var (vm, svcMock, _, _) = Crear(productos);

        await vm.CargarAsync();

        svcMock.Verify(s => s.BuscarAsync(null, null, null), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("P001", vm.Items[0].Codigo);
    }

    [Fact]
    public async Task Buscar_FiltroTexto_InvocaBuscarPorTextoAsyncConElTermino()
    {
        // El buscador debe matchear por Nombre, SKU o código de barras (lógica OR),
        // por eso delega en BuscarPorTextoAsync y no en BuscarAsync(sku, codigoBarras, nombre).
        var (vm, svcMock, _, _) = Crear();
        vm.FiltroBusqueda = "aceite";

        await vm.BuscarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BuscarPorTextoAsync("aceite"), Times.Once);
    }

    [Fact]
    public async Task FiltroBusqueda_AlCambiar_DisparaBusquedaAutomaticaConDebounce()
    {
        var (vm, svcMock, _, _) = Crear();

        vm.FiltroBusqueda = "aceite";
        await vm._tareaDebounce;

        svcMock.Verify(s => s.BuscarPorTextoAsync("aceite"), Times.Once);
    }

    [Fact]
    public async Task FiltroBusqueda_CambiosRapidosSeguidos_CancelaBusquedaObsoleta()
    {
        var (vm, svcMock, _, _) = Crear();

        vm.FiltroBusqueda = "ace";
        vm.FiltroBusqueda = "aceite";
        await vm._tareaDebounce;

        svcMock.Verify(s => s.BuscarPorTextoAsync("aceite"), Times.Once);
        svcMock.Verify(s => s.BuscarPorTextoAsync("ace"), Times.Never);
        svcMock.Verify(s => s.BuscarPorTextoAsync(It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAProductoFormViewModel()
    {
        var (vm, _, navMock, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<ProductoFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task CargarAsync_SinProductos_ItemsVacio()
    {
        var (vm, _, _, _) = Crear(new List<ProductoDto>());

        await vm.CargarAsync();

        Assert.Empty(vm.Items);
    }

    // ── ItemsView: fix de ordenamiento por click en encabezados (Avalonia 12, regresión #21129) ──

    [Fact]
    public async Task ItemsView_EsOrdenable()
    {
        var productos = new List<ProductoDto>
        {
            CrearProductoDto(1, "P001", "Producto Uno"),
            CrearProductoDto(2, "P002", "Producto Dos")
        };
        var (vm, _, _, _) = Crear(productos);

        await vm.CargarAsync();

        Assert.NotNull(vm.ItemsView);
        Assert.IsType<DataGridCollectionView>(vm.ItemsView);
        Assert.True(vm.ItemsView.CanSort);
    }

    [Fact]
    public async Task ItemsView_AlAplicarSortDescription_OrdenaLosItems()
    {
        var desordenados = new List<ProductoDto>
        {
            CrearProductoDto(1, "P003", "Zapallo"),
            CrearProductoDto(2, "P001", "Aceite"),
            CrearProductoDto(3, "P002", "Manteca")
        };
        var (vm, _, _, _) = Crear(desordenados);
        await vm.CargarAsync();

        vm.ItemsView.SortDescriptions.Add(
            DataGridSortDescription.FromPath(nameof(ProductoDto.Nombre), ListSortDirection.Ascending));

        var ordenados = vm.ItemsView.Cast<ProductoDto>().ToList();
        Assert.Equal(3, ordenados.Count);
        Assert.Equal("Aceite", ordenados[0].Nombre);
        Assert.Equal("Manteca", ordenados[1].Nombre);
        Assert.Equal("Zapallo", ordenados[2].Nombre);
    }

    [Fact]
    public async Task Items_TrasRecarga_SeReflejanEnItemsView()
    {
        var (vm, svcMock, _, _) = Crear(new List<ProductoDto> { CrearProductoDto(1, "P001", "Uno") });
        await vm.CargarAsync();
        Assert.Single(vm.ItemsView.Cast<ProductoDto>());

        var nuevaLista = new List<ProductoDto>
        {
            CrearProductoDto(10, "P010", "Diez"),
            CrearProductoDto(11, "P011", "Once"),
            CrearProductoDto(12, "P012", "Doce")
        };
        svcMock
            .Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(nuevaLista);

        await vm.CargarAsync();

        Assert.Equal(3, vm.Items.Count);
        Assert.Equal(3, vm.ItemsView.Cast<ProductoDto>().Count());
    }

    // ── BajaCommand: baja lógica con confirmación (mismo patrón que Categoria/Proveedor/UnidadMedida) ──

    [Fact]
    public async Task BajaCommand_ConItemSeleccionado_PideConfirmacionYLlamaServicio()
    {
        var producto = CrearProductoDto(5, "P005", "Prueba");
        var (vm, svcMock, _, confirmMock) = Crear(new List<ProductoDto> { producto });
        await vm.CargarAsync();
        vm.ItemSeleccionado = producto;

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svcMock.Verify(s => s.BajaLogicaAsync(5), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_SiNoConfirma_NoLlamaServicio()
    {
        var producto = CrearProductoDto(5, "P005", "Prueba");
        var (vm, svcMock, _, confirmMock) = Crear(new List<ProductoDto> { producto });
        await vm.CargarAsync();
        vm.ItemSeleccionado = producto;
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.BajaCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BajaLogicaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaInvalidOperationException_NoPropagaYInforma()
    {
        var producto = CrearProductoDto(5, "P005", "Prueba");
        var (vm, svcMock, _, confirmMock) = Crear(new List<ProductoDto> { producto });
        await vm.CargarAsync();
        vm.ItemSeleccionado = producto;

        var mensaje = "El producto 5 ya está inactivo.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new InvalidOperationException(mensaje));

        // No debe propagar la excepción (regresión real del crash en las otras entidades de catálogo).
        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaKeyNotFoundException_NoPropagaYInforma()
    {
        var producto = CrearProductoDto(5, "P005", "Prueba");
        var (vm, svcMock, _, confirmMock) = Crear(new List<ProductoDto> { producto });
        await vm.CargarAsync();
        vm.ItemSeleccionado = producto;

        var mensaje = "Producto 5 no encontrado.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new KeyNotFoundException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public void BajaCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemInactivo_EstaDeshabilitado()
    {
        var producto = CrearProductoDto(5, "P005", "Prueba", activo: false);
        var (vm, _, _, _) = Crear(new List<ProductoDto> { producto });
        await vm.CargarAsync();
        vm.ItemSeleccionado = producto;

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemActivo_EstaHabilitado()
    {
        var producto = CrearProductoDto(5, "P005", "Prueba");
        var (vm, _, _, _) = Crear(new List<ProductoDto> { producto });
        await vm.CargarAsync();
        vm.ItemSeleccionado = producto;

        Assert.True(vm.BajaCommand.CanExecute(null));
    }

    // ── EditarCommand: navega al form en modo edición con el producto seleccionado ──

    [Fact]
    public void EditarCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task EditarCommand_ConItemSeleccionado_EstaHabilitado()
    {
        var producto = CrearProductoDto(5, "P005", "Prueba");
        var (vm, _, _, _) = Crear(new List<ProductoDto> { producto });
        await vm.CargarAsync();
        vm.ItemSeleccionado = producto;

        Assert.True(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task EditarCommand_ItemInactivo_EstaDeshabilitado()
    {
        // Regla de negocio: un producto dado de baja (Activo=false) no debe poder editarse,
        // igual que ya ocurre con BajaCommand.
        var producto = CrearProductoDto(5, "P005", "Prueba", activo: false);
        var (vm, _, _, _) = Crear(new List<ProductoDto> { producto });
        await vm.CargarAsync();
        vm.ItemSeleccionado = producto;

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task EditarCommand_ItemActivo_EstaHabilitado()
    {
        var producto = CrearProductoDto(5, "P005", "Prueba");
        var (vm, _, _, _) = Crear(new List<ProductoDto> { producto });
        await vm.CargarAsync();
        vm.ItemSeleccionado = producto;

        Assert.True(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task EditarCommand_ConItemSeleccionado_NavegaAProductoFormViewModelConInicializador()
    {
        var producto = CrearProductoDto(5, "P005", "Prueba");
        var (vm, _, navMock, _) = Crear(new List<ProductoDto> { producto });
        await vm.CargarAsync();
        vm.ItemSeleccionado = producto;

        await vm.EditarCommand.ExecuteAsync(null);

        navMock.Verify(
            n => n.Navegar<ProductoFormViewModel>(It.IsAny<Action<ProductoFormViewModel>>()),
            Times.Once);
    }

    [Fact]
    public async Task EditarCommand_ElInicializadorPrecargaElProductoSeleccionado()
    {
        var producto = CrearProductoDto(5, "P005", "Prueba", unidadMedidaId: 1, precioCosto: 10, precioVenta: 20);
        var (vm, _, navMock, _) = Crear(new List<ProductoDto> { producto });
        await vm.CargarAsync();
        vm.ItemSeleccionado = producto;

        Action<ProductoFormViewModel>? inicializadorCapturado = null;
        navMock
            .Setup(n => n.Navegar<ProductoFormViewModel>(It.IsAny<Action<ProductoFormViewModel>>()))
            .Callback<Action<ProductoFormViewModel>>(a => inicializadorCapturado = a);

        await vm.EditarCommand.ExecuteAsync(null);

        Assert.NotNull(inicializadorCapturado);

        var formVm = new ProductoFormViewModel(
            new Mock<IProductoService>().Object,
            new Mock<IUnidadMedidaService>().Object,
            new Mock<ICategoriaService>().Object,
            new Mock<INavigationService>().Object);

        inicializadorCapturado!(formVm);

        Assert.True(formVm.EsEdicion);
        Assert.Equal("P005", formVm.Codigo);
        Assert.Equal("Prueba", formVm.Nombre);
    }
}

public class ProductoFormViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static readonly UnidadMedida UnidadPorDefecto =
        new() { Id = 1, Nombre = "Unidad", Abreviatura = "u", Activo = true };

    private static ProductoDto CrearProductoDto(
        int id = 9, string codigo = "P009", string nombre = "Aceite",
        string? codigoBarras = null, string? descripcion = null,
        decimal precioCosto = 0m, decimal precioVenta = 0m,
        int unidadMedidaId = 0, int? categoriaId = null, int? proveedorId = null,
        decimal stockMinimo = 0m)
        => new ProductoDto(
            Id: id, Codigo: codigo, CodigoBarras: codigoBarras, Nombre: nombre, Descripcion: descripcion,
            CategoriaId: categoriaId, CategoriaNombre: null, ProveedorId: proveedorId,
            UnidadMedidaId: unidadMedidaId, UnidadMedidaNombre: "", PrecioCosto: precioCosto,
            PrecioVenta: precioVenta, StockActual: 0m, StockMinimo: stockMinimo, Activo: true,
            FechaAlta: default);

    private static (ProductoFormViewModel vm,
                    Mock<IProductoService> svcMock,
                    Mock<IUnidadMedidaService> umSvcMock,
                    Mock<ICategoriaService> catSvcMock,
                    Mock<INavigationService> navMock)
        Crear(IReadOnlyList<UnidadMedida>? unidades = null, IReadOnlyList<Categoria>? categorias = null)
    {
        var svcMock = new Mock<IProductoService>();
        svcMock
            .Setup(s => s.AltaAsync(It.IsAny<Producto>()))
            .ReturnsAsync(1);
        svcMock
            .Setup(s => s.ModificarAsync(It.IsAny<Producto>()))
            .Returns(Task.CompletedTask);
        svcMock
            .Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<ProductoDto>());

        var umSvcMock = new Mock<IUnidadMedidaService>();
        umSvcMock
            .Setup(s => s.GarantizarUnidadPorDefectoAsync())
            .ReturnsAsync(UnidadPorDefecto);
        umSvcMock
            .Setup(s => s.ListarActivasAsync())
            .ReturnsAsync(unidades ?? new List<UnidadMedida> { UnidadPorDefecto });

        var catSvcMock = new Mock<ICategoriaService>();
        catSvcMock
            .Setup(s => s.ListarActivasAsync())
            .ReturnsAsync(categorias ?? new List<Categoria>());

        var navMock = new Mock<INavigationService>();
        var vm = new ProductoFormViewModel(svcMock.Object, umSvcMock.Object, catSvcMock.Object, navMock.Object);
        return (vm, svcMock, umSvcMock, catSvcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_DatosCompletos_LlamaAltaAsync()
    {
        var (vm, svcMock, _, _, _) = Crear();
        vm.Codigo = "P001";
        vm.Nombre = "Producto Test";
        vm.UnidadMedidaSeleccionada = UnidadPorDefecto;

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<Producto>(p =>
            p.Codigo == "P001" && p.Nombre == "Producto Test")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_Exitoso_NavegaAListado()
    {
        var (vm, _, _, _, navMock) = Crear();
        vm.Codigo = "P001";
        vm.Nombre = "Producto Test";
        vm.UnidadMedidaSeleccionada = UnidadPorDefecto;

        await vm.GuardarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<ProductoListViewModel>(), Times.Once);
    }

    [Fact]
    public void GuardarCommand_SinCodigo_EstaDeshabilitado()
    {
        var (vm, _, _, _, _) = Crear();
        vm.Nombre = "Producto Test";
        vm.UnidadMedidaSeleccionada = UnidadPorDefecto;

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public void GuardarCommand_SinNombre_EstaDeshabilitado()
    {
        var (vm, _, _, _, _) = Crear();
        vm.Codigo = "P001";
        vm.UnidadMedidaSeleccionada = UnidadPorDefecto;

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public void GuardarCommand_SinUnidadMedidaSeleccionada_EstaDeshabilitado()
    {
        var (vm, _, _, _, _) = Crear();
        vm.Codigo = "P001";
        vm.Nombre = "Producto Test";

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task GuardarAsync_AsignaUnidadMedidaIdYCategoriaId()
    {
        var categoria = new Categoria { Id = 7, Nombre = "Bebidas", Activo = true };
        var (vm, svcMock, _, _, _) = Crear();
        vm.Codigo = "P001";
        vm.Nombre = "Producto Test";
        vm.UnidadMedidaSeleccionada = UnidadPorDefecto;
        vm.CategoriaSeleccionada = categoria;

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<Producto>(p =>
            p.UnidadMedidaId == UnidadPorDefecto.Id && p.CategoriaId == 7)), Times.Once);
    }

    [Fact]
    public async Task GuardarAsync_SinCategoriaSeleccionada_CategoriaIdEsNull()
    {
        var (vm, svcMock, _, _, _) = Crear();
        vm.Codigo = "P001";
        vm.Nombre = "Producto Test";
        vm.UnidadMedidaSeleccionada = UnidadPorDefecto;

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<Producto>(p => p.CategoriaId == null)), Times.Once);
    }

    // ── InicializarAsync: seed idempotente de "Unidad" por defecto ────────────

    [Fact]
    public async Task InicializarAsync_SinUnidades_GarantizaYPreseleccionaUnidadPorDefecto()
    {
        var (vm, _, umSvcMock, _, _) = Crear(unidades: new List<UnidadMedida> { UnidadPorDefecto });

        await vm.InicializarAsync();

        umSvcMock.Verify(s => s.GarantizarUnidadPorDefectoAsync(), Times.Once);
        Assert.Single(vm.UnidadesMedida);
        Assert.NotNull(vm.UnidadMedidaSeleccionada);
        Assert.Equal("Unidad", vm.UnidadMedidaSeleccionada!.Nombre);
    }

    [Fact]
    public async Task InicializarAsync_ConUnidadesExistentes_NoDuplicaYPreseleccionaUnidad()
    {
        var kilogramo = new UnidadMedida { Id = 2, Nombre = "Kilogramo", Abreviatura = "kg", Activo = true };
        var (vm, _, umSvcMock, _, _) = Crear(unidades: new List<UnidadMedida> { UnidadPorDefecto, kilogramo });

        await vm.InicializarAsync();

        umSvcMock.Verify(s => s.GarantizarUnidadPorDefectoAsync(), Times.Once);
        Assert.Equal(2, vm.UnidadesMedida.Count);
        Assert.NotNull(vm.UnidadMedidaSeleccionada);
        Assert.Equal("Unidad", vm.UnidadMedidaSeleccionada!.Nombre);
    }

    [Fact]
    public async Task InicializarAsync_PopulaCategorias()
    {
        var categorias = new List<Categoria> { new() { Id = 1, Nombre = "Bebidas", Activo = true } };
        var (vm, _, _, _, _) = Crear(categorias: categorias);

        await vm.InicializarAsync();

        Assert.Single(vm.Categorias);
        Assert.Equal("Bebidas", vm.Categorias[0].Nombre);
        Assert.Null(vm.CategoriaSeleccionada);
    }

    // ── Modo edición: CargarParaEditar + bifurcación de GuardarAsync ───────────

    [Fact]
    public void Titulo_PorDefecto_EsNuevoProducto()
    {
        var (vm, _, _, _, _) = Crear();

        Assert.Equal("Nuevo producto", vm.Titulo);
        Assert.False(vm.EsEdicion);
    }

    [Fact]
    public void CargarParaEditar_SeteaEsEdicionYCamposDelProducto()
    {
        var (vm, _, _, _, _) = Crear();
        var producto = CrearProductoDto(
            codigoBarras: "7791234567890", descripcion: "Aceite de girasol",
            precioCosto: 100m, precioVenta: 150m);

        vm.CargarParaEditar(producto);

        Assert.True(vm.EsEdicion);
        Assert.Equal("P009", vm.Codigo);
        Assert.Equal("Aceite", vm.Nombre);
        Assert.Equal("7791234567890", vm.CodigoBarras);
        Assert.Equal("Aceite de girasol", vm.Descripcion);
        Assert.Equal(100m, vm.PrecioCosto);
        Assert.Equal(150m, vm.PrecioVenta);
    }

    [Fact]
    public void CargarParaEditar_CambiaTituloAEditarProducto()
    {
        var (vm, _, _, _, _) = Crear();

        vm.CargarParaEditar(CrearProductoDto());

        Assert.Equal("Editar producto", vm.Titulo);
    }

    [Fact]
    public async Task InicializarAsync_ModoEdicion_PreseleccionaUnidadYCategoriaOriginales()
    {
        var kilogramo = new UnidadMedida { Id = 2, Nombre = "Kilogramo", Abreviatura = "kg", Activo = true };
        var bebidas = new Categoria { Id = 7, Nombre = "Bebidas", Activo = true };
        var (vm, _, _, _, _) = Crear(
            unidades: new List<UnidadMedida> { UnidadPorDefecto, kilogramo },
            categorias: new List<Categoria> { bebidas });

        vm.CargarParaEditar(CrearProductoDto(unidadMedidaId: kilogramo.Id, categoriaId: bebidas.Id));

        await vm.InicializarAsync();

        Assert.Same(kilogramo, vm.UnidadMedidaSeleccionada);
        Assert.Same(bebidas, vm.CategoriaSeleccionada);
    }

    [Fact]
    public async Task InicializarAsync_ModoEdicion_SinCategoriaOriginal_CategoriaSeleccionadaEsNull()
    {
        var (vm, _, _, _, _) = Crear();

        vm.CargarParaEditar(CrearProductoDto(unidadMedidaId: UnidadPorDefecto.Id, categoriaId: null));

        await vm.InicializarAsync();

        Assert.Null(vm.CategoriaSeleccionada);
    }

    [Fact]
    public async Task GuardarCommand_ModoEdicion_LlamaModificarAsyncNoAltaAsync()
    {
        var (vm, svcMock, _, _, _) = Crear();
        vm.CargarParaEditar(CrearProductoDto(unidadMedidaId: UnidadPorDefecto.Id));
        await vm.InicializarAsync();

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.ModificarAsync(It.Is<Producto>(p => p.Id == 9)), Times.Once);
        svcMock.Verify(s => s.AltaAsync(It.IsAny<Producto>()), Times.Never);
    }

    [Fact]
    public async Task GuardarCommand_ModoEdicion_PreservaProveedorIdYStockMinimoOriginales()
    {
        var (vm, svcMock, _, _, _) = Crear();
        vm.CargarParaEditar(CrearProductoDto(unidadMedidaId: UnidadPorDefecto.Id, proveedorId: 3, stockMinimo: 12m));
        await vm.InicializarAsync();

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.ModificarAsync(It.Is<Producto>(p =>
            p.ProveedorId == 3 && p.StockMinimo == 12m)), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_ModoEdicion_Exitoso_NavegaAListado()
    {
        var (vm, _, _, _, navMock) = Crear();
        vm.CargarParaEditar(CrearProductoDto(unidadMedidaId: UnidadPorDefecto.Id));
        await vm.InicializarAsync();

        await vm.GuardarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<ProductoListViewModel>(), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_ModoEdicion_ServicioLanzaExcepcionDeDominio_MuestraMensajeErrorYNoCrashea()
    {
        var (vm, svcMock, _, _, _) = Crear();
        vm.CargarParaEditar(CrearProductoDto(unidadMedidaId: UnidadPorDefecto.Id));
        await vm.InicializarAsync();

        var mensaje = "Ya existe un producto con el código de barras '7791234567890'.";
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<Producto>()))
            .ThrowsAsync(new InvalidOperationException(mensaje));

        // No debe propagar la excepción — se muestra amigable en MensajeError, igual que AltaAsync.
        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal(mensaje, vm.MensajeError);
    }

    [Fact]
    public async Task GuardarCommand_ModoAlta_SigueFuncionandoTrasAgregarModoEdicion()
    {
        // Regresión: el bifurcado por EsEdicion no debe romper el flujo de alta existente.
        var (vm, svcMock, _, _, navMock) = Crear();
        vm.Codigo = "P010";
        vm.Nombre = "Producto Nuevo";
        vm.UnidadMedidaSeleccionada = UnidadPorDefecto;

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.IsAny<Producto>()), Times.Once);
        svcMock.Verify(s => s.ModificarAsync(It.IsAny<Producto>()), Times.Never);
        navMock.Verify(n => n.Navegar<ProductoListViewModel>(), Times.Once);
    }
}
