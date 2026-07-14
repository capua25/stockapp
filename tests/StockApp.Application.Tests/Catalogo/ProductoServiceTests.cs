using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Catalogo;

public class ProductoServiceTests
{
    private static (ProductoService svc,
                    Mock<IProductoRepository> repoMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock,
                    Mock<IAuditLogger> auditMock,
                    Mock<IUnidadMedidaRepository> umRepoMock)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1)
    {
        var repo    = new Mock<IProductoRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();
        var umRepo  = new Mock<IUnidadMedidaRepository>();

        session.Setup(s => s.RolActual).Returns(rol);
        var sesionHelper = new StockApp.Application.Auth.UsuarioSesion(idSesion, "usuario", rol, null);
        session.Setup(s => s.UsuarioActual).Returns(sesionHelper);

        if (rol == RolUsuario.Admin)
            auth.Setup(a => a.Verificar(RolUsuario.Admin, It.IsAny<string>()));
        else
            auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.GestionarProductos));

        // Por defecto la unidad de medida con id > 0 existe — evita romper tests existentes
        umRepo.Setup(r => r.ObtenerPorIdAsync(It.IsAny<int>()))
              .ReturnsAsync(new UnidadMedida { Id = 1, Nombre = "Unidad", Abreviatura = "u" });

        var svc = new ProductoService(repo.Object, session.Object, auth.Object, audit.Object, umRepo.Object, Mock.Of<IVersionReportes>());
        return (svc, repo, session, auth, audit, umRepo);
    }

    // ─── Alta ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AltaAsync_CodigoDuplicado_LanzaInvalidOperation()
    {
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.ExisteCodigoAsync("SKU-001", null)).ReturnsAsync(true);

        var p = new Producto { Codigo = "SKU-001", Nombre = "Fideos", UnidadMedidaId = 1 };
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AltaAsync(p));
    }

    [Fact]
    public async Task AltaAsync_CodigoBarrasDuplicado_LanzaInvalidOperation()
    {
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.ExisteCodigoAsync("SKU-002", null)).ReturnsAsync(false);
        repo.Setup(r => r.ExisteCodigoBarrasAsync("7891234567890", null)).ReturnsAsync(true);

        var p = new Producto { Codigo = "SKU-002", Nombre = "Fideos", UnidadMedidaId = 1, CodigoBarras = "7891234567890" };
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AltaAsync(p));
    }

    [Fact]
    public async Task AltaAsync_PrecioNegativo_LanzaArgumentException()
    {
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.ExisteCodigoAsync(It.IsAny<string>(), null)).ReturnsAsync(false);

        var p = new Producto { Codigo = "SKU-003", Nombre = "Fideos", UnidadMedidaId = 1, PrecioCosto = -1m };
        await Assert.ThrowsAsync<ArgumentException>(() => svc.AltaAsync(p));
    }

    [Fact]
    public async Task AltaAsync_PrecioVentaNegativo_LanzaArgumentException()
    {
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.ExisteCodigoAsync(It.IsAny<string>(), null)).ReturnsAsync(false);

        var p = new Producto { Codigo = "SKU-004", Nombre = "Fideos", UnidadMedidaId = 1, PrecioVenta = -0.01m };
        await Assert.ThrowsAsync<ArgumentException>(() => svc.AltaAsync(p));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RetornaId_RegistraAuditoria()
    {
        var (svc, repo, _, _, audit, _) = Crear();
        repo.Setup(r => r.ExisteCodigoAsync("SKU-001", null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<Producto>())).ReturnsAsync(42);

        var p = new Producto { Codigo = "SKU-001", Nombre = "Fideos", UnidadMedidaId = 1, PrecioVenta = 150m };
        var id = await svc.AltaAsync(p);

        Assert.Equal(42, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaProducto,
            "Producto", 42, It.IsAny<string>()), Times.Once);
    }

    // ─── Alta: FechaAlta ─────────────────────────────────────────────────────

    [Fact]
    public async Task AltaAsync_Exitosa_SeteaFechaAltaConValorActual()
    {
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.ExisteCodigoAsync("SKU-FA1", null)).ReturnsAsync(false);

        Producto? capturado = null;
        repo.Setup(r => r.AgregarAsync(It.IsAny<Producto>()))
            .Callback<Producto>(p => capturado = p)
            .ReturnsAsync(1);

        var antes = DateTime.UtcNow.AddSeconds(-1);
        var p = new Producto { Codigo = "SKU-FA1", Nombre = "Fideos", UnidadMedidaId = 1 };
        await svc.AltaAsync(p);
        var despues = DateTime.UtcNow.AddSeconds(1);

        Assert.NotNull(capturado);
        Assert.NotEqual(default, capturado!.FechaAlta);
        Assert.InRange(capturado.FechaAlta, antes, despues);
    }

    // ─── Modificar ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_GranularPorCampo_GeneraDetalleCorrect()
    {
        var original = new Producto
        {
            Id = 5, Codigo = "SKU-001", Nombre = "Fideos",
            UnidadMedidaId = 1, PrecioVenta = 100m, PrecioCosto = 50m, Activo = true
        };
        var (svc, repo, _, _, audit, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteCodigoBarrasAsync(It.IsAny<string>(), It.IsAny<int?>())).ReturnsAsync(false);

        var actualizado = new Producto
        {
            Id = 5, Codigo = "SKU-001", Nombre = "Fideos finos",
            UnidadMedidaId = 1, PrecioVenta = 100m, PrecioCosto = 50m, Activo = true
        };
        await svc.ModificarAsync(actualizado);

        // Debe auditar ModificacionProducto con diff de Nombre
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionProducto,
            "Producto", 5, It.Is<string>(d => d.Contains("Nombre"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_SinCambios_NoAudita()
    {
        var original = new Producto
        {
            Id = 5, Codigo = "SKU-001", Nombre = "Fideos",
            UnidadMedidaId = 1, PrecioVenta = 100m, PrecioCosto = 50m, Activo = true
        };
        var (svc, repo, _, _, audit, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteCodigoBarrasAsync(It.IsAny<string>(), It.IsAny<int?>())).ReturnsAsync(false);

        // Pasar exactamente los mismos valores
        var sinCambios = new Producto
        {
            Id = 5, Codigo = "SKU-001", Nombre = "Fideos",
            UnidadMedidaId = 1, PrecioVenta = 100m, PrecioCosto = 50m, Activo = true
        };
        await svc.ModificarAsync(sinCambios);

        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), It.IsAny<AccionAuditada>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ModificarAsync_CodigoBarrasDuplicado_LanzaInvalidOperation()
    {
        var original = new Producto
        {
            Id = 3, Codigo = "SKU-003", Nombre = "Arroz",
            UnidadMedidaId = 1, CodigoBarras = "111", Activo = true
        };
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(3)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteCodigoBarrasAsync("222", 3)).ReturnsAsync(true);

        var mod = new Producto
        {
            Id = 3, Codigo = "SKU-003", Nombre = "Arroz",
            UnidadMedidaId = 1, CodigoBarras = "222", Activo = true
        };
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.ModificarAsync(mod));
    }

    // ─── Baja lógica ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BajaLogicaAsync_Exitosa_ActivoFalse_Audita()
    {
        var p = new Producto { Id = 5, Codigo = "SKU-001", Nombre = "Fideos", UnidadMedidaId = 1, Activo = true };
        var (svc, repo, _, _, audit, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(p);

        await svc.BajaLogicaAsync(5);

        repo.Verify(r => r.ActualizarAsync(It.Is<Producto>(x => x.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaProducto,
            "Producto", 5, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactivo_LanzaInvalidOperation()
    {
        var p = new Producto { Id = 5, Codigo = "SKU-001", Nombre = "Fideos", UnidadMedidaId = 1, Activo = false };
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(p);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(5));
    }

    // ─── Cambio de precio ────────────────────────────────────────────────────

    [Fact]
    public async Task CambiarPrecioAsync_RegistraCambioPrecio()
    {
        var p = new Producto
        {
            Id = 7, Codigo = "SKU-007", Nombre = "Pan",
            UnidadMedidaId = 1, PrecioCosto = 50m, PrecioVenta = 100m, Activo = true
        };
        var (svc, repo, _, _, audit, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(7)).ReturnsAsync(p);

        await svc.CambiarPrecioAsync(7, 55m, 110m);

        repo.Verify(r => r.ActualizarAsync(It.Is<Producto>(x =>
            x.PrecioCosto == 55m && x.PrecioVenta == 110m)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.CambioPrecio,
            "Producto", 7, It.IsAny<string>()), Times.Once);
    }

    // ─── Búsqueda ────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuscarAsync_PorSku_RetornaProducto()
    {
        var lista = new List<Producto>
        {
            new() { Id = 1, Codigo = "SKU-001", Nombre = "Fideos", UnidadMedidaId = 1, Activo = true }
        };
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.BuscarAsync("SKU-001", null, null)).ReturnsAsync(lista.AsReadOnly());

        var resultado = await svc.BuscarAsync("SKU-001", null, null);

        Assert.Single(resultado);
        Assert.Equal("SKU-001", resultado[0].Codigo);
    }

    [Fact]
    public async Task BuscarAsync_PorNombreParcial_RetornaMultiples()
    {
        var lista = new List<Producto>
        {
            new() { Id = 1, Nombre = "Fideos finos", UnidadMedidaId = 1, Activo = true },
            new() { Id = 2, Nombre = "Fideos gruesos", UnidadMedidaId = 1, Activo = true }
        };
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.BuscarAsync(null, null, "fideos")).ReturnsAsync(lista.AsReadOnly());

        var resultado = await svc.BuscarAsync(null, null, "fideos");

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public async Task BuscarAsync_SinResultados_RetornaListaVacia()
    {
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.BuscarAsync(null, null, "xyz999"))
            .ReturnsAsync(new List<Producto>().AsReadOnly());

        var resultado = await svc.BuscarAsync(null, null, "xyz999");

        Assert.Empty(resultado);
    }

    // ─── Búsqueda por término único (OR entre Codigo/CodigoBarras/Nombre) ────

    [Fact]
    public async Task BuscarPorTextoAsync_DelegaAlRepositorioConElTermino()
    {
        var lista = new List<Producto>
        {
            new() { Id = 1, Codigo = "SKU-001", Nombre = "Fideos", UnidadMedidaId = 1, Activo = true }
        };
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.BuscarPorTextoAsync("fideos")).ReturnsAsync(lista.AsReadOnly());

        var resultado = await svc.BuscarPorTextoAsync("fideos");

        Assert.Single(resultado);
        repo.Verify(r => r.BuscarPorTextoAsync("fideos"), Times.Once);
    }

    [Fact]
    public async Task BuscarPorTextoAsync_TerminoNulo_DelegaYRetornaTodos()
    {
        var lista = new List<Producto>
        {
            new() { Id = 1, Nombre = "A", UnidadMedidaId = 1, Activo = true },
            new() { Id = 2, Nombre = "B", UnidadMedidaId = 1, Activo = true }
        };
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.BuscarPorTextoAsync(null)).ReturnsAsync(lista.AsReadOnly());

        var resultado = await svc.BuscarPorTextoAsync(null);

        Assert.Equal(2, resultado.Count);
    }

    // ─── Control cruzado auth ─────────────────────────────────────────────────

    [Fact]
    public async Task Operador_TieneGestionarProductos_NoLanzaUnauthorized()
    {
        var (svc, repo, _, auth, _, _) = Crear(RolUsuario.Operador, idSesion: 2);
        repo.Setup(r => r.ExisteCodigoAsync("SKU-OP", null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<Producto>())).ReturnsAsync(10);

        // No debe lanzar UnauthorizedAccessException
        var p = new Producto { Codigo = "SKU-OP", Nombre = "Fideos", UnidadMedidaId = 1 };
        var ex = await Record.ExceptionAsync(() => svc.AltaAsync(p));

        Assert.Null(ex);
        auth.Verify(a => a.Verificar(RolUsuario.Operador, Permisos.GestionarProductos), Times.Once);
    }

    // ─── Validación de existencia de UnidadMedida (W2) ───────────────────────

    [Fact]
    public async Task AltaAsync_UnidadMedidaInexistente_LanzaArgumentException()
    {
        // UnidadMedidaId = 99 (Id > 0 pero no existe en la BD)
        var (svc, repo, _, _, _, umRepo) = Crear();
        umRepo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((UnidadMedida?)null);

        var p = new Producto { Codigo = "SKU-UM", Nombre = "Fideos", UnidadMedidaId = 99 };
        await Assert.ThrowsAsync<ArgumentException>(() => svc.AltaAsync(p));
    }

    [Fact]
    public async Task ModificarAsync_UnidadMedidaInexistente_LanzaArgumentException()
    {
        // Modificar cambiando UnidadMedidaId a uno que no existe
        var original = new Producto
        {
            Id = 10, Codigo = "SKU-010", Nombre = "Arroz",
            UnidadMedidaId = 1, Activo = true
        };
        var (svc, repo, _, _, _, umRepo) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(10)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteCodigoBarrasAsync(It.IsAny<string>(), It.IsAny<int?>())).ReturnsAsync(false);
        umRepo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((UnidadMedida?)null);

        var modificado = new Producto
        {
            Id = 10, Codigo = "SKU-010", Nombre = "Arroz",
            UnidadMedidaId = 99, Activo = true
        };
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ModificarAsync(modificado));
    }

    // ─── Mapeo a ProductoDto (Fase 0 — migración client-server) ──────────────

    [Fact]
    public async Task BuscarAsync_MapeaANombresPlanosDeCategoriaYUnidadMedida()
    {
        var categoria = new Categoria { Id = 3, Nombre = "Almacén", Activo = true };
        var unidad = new UnidadMedida { Id = 1, Nombre = "Kilogramo", Abreviatura = "kg", Activo = true };
        var producto = new Producto
        {
            Id = 1, Codigo = "SKU-001", CodigoBarras = "111", Nombre = "Fideos",
            Descripcion = "Fideos secos", CategoriaId = 3, Categoria = categoria,
            ProveedorId = 9, UnidadMedidaId = 1, UnidadMedida = unidad,
            PrecioCosto = 10m, PrecioVenta = 20m, StockActual = 5m, StockMinimo = 2m,
            Activo = true, FechaAlta = new DateTime(2026, 1, 1)
        };
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.BuscarAsync(null, null, null)).ReturnsAsync(new List<Producto> { producto });

        var resultado = await svc.BuscarAsync(null, null, null);

        var dto = Assert.Single(resultado);
        Assert.Equal(1, dto.Id);
        Assert.Equal("SKU-001", dto.Codigo);
        Assert.Equal("111", dto.CodigoBarras);
        Assert.Equal("Fideos", dto.Nombre);
        Assert.Equal("Fideos secos", dto.Descripcion);
        Assert.Equal(3, dto.CategoriaId);
        Assert.Equal("Almacén", dto.CategoriaNombre);
        Assert.Equal(9, dto.ProveedorId);
        Assert.Equal(1, dto.UnidadMedidaId);
        Assert.Equal("Kilogramo", dto.UnidadMedidaNombre);
        Assert.Equal(10m, dto.PrecioCosto);
        Assert.Equal(20m, dto.PrecioVenta);
        Assert.Equal(5m, dto.StockActual);
        Assert.Equal(2m, dto.StockMinimo);
        Assert.True(dto.Activo);
        Assert.Equal(new DateTime(2026, 1, 1), dto.FechaAlta);
    }

    [Fact]
    public async Task BuscarAsync_ProductoSinCategoriaCargada_CategoriaNombreEsNull()
    {
        var unidad = new UnidadMedida { Id = 1, Nombre = "Unidad", Abreviatura = "u", Activo = true };
        var producto = new Producto
        {
            Id = 2, Codigo = "SKU-002", Nombre = "Arroz", CategoriaId = null, Categoria = null,
            UnidadMedidaId = 1, UnidadMedida = unidad, Activo = true
        };
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.BuscarAsync(null, null, null)).ReturnsAsync(new List<Producto> { producto });

        var resultado = await svc.BuscarAsync(null, null, null);

        Assert.Null(Assert.Single(resultado).CategoriaNombre);
    }

    [Fact]
    public async Task BuscarPorTextoAsync_TambienMapeaAProductoDto()
    {
        var producto = new Producto { Id = 4, Codigo = "SKU-004", Nombre = "Fideos", UnidadMedidaId = 1, Activo = true };
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.BuscarPorTextoAsync("fideos")).ReturnsAsync(new List<Producto> { producto });

        var resultado = await svc.BuscarPorTextoAsync("fideos");

        Assert.Equal("SKU-004", Assert.Single(resultado).Codigo);
    }

    // ─── EntidadNoEncontradaException (Fase 3a, D4) ─────────────────────────

    [Fact]
    public async Task ModificarAsync_ProductoInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Producto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ModificarAsync(new Producto { Id = 99, Codigo = "X", Nombre = "X", UnidadMedidaId = 1 }));
    }

    [Fact]
    public async Task BajaLogicaAsync_ProductoInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Producto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.BajaLogicaAsync(99));
    }
}

