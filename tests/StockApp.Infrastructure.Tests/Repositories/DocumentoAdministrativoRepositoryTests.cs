using Microsoft.EntityFrameworkCore;
using StockApp.Application.Documentos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class DocumentoAdministrativoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly DocumentoAdministrativoRepository _repo;

    public DocumentoAdministrativoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new DocumentoAdministrativoRepository(Context);
    }

    private async Task<int> SeedUsuarioAsync(string nombreUsuario = "operario1")
    {
        var usuario = new Usuario
        {
            NombreUsuario = nombreUsuario,
            HashContrasena = "x",
            Rol = RolUsuario.Operador,
            FechaAlta = DateTime.UtcNow,
        };
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();
        return usuario.Id;
    }

    private static DocumentoAdministrativo NuevoDocumento(
        int registradoPorUsuarioId, TipoDocumento tipo = TipoDocumento.Expediente,
        int anio = 2026, string numero = "0087", EstadoDocumento estado = EstadoDocumento.Pendiente,
        string descripcion = "Solicitud de poda de árbol en vereda") => new()
    {
        Numero = numero,
        Anio = anio,
        Tipo = tipo,
        FechaEmision = DateTime.UtcNow,
        Descripcion = descripcion,
        Estado = estado,
        RegistradoPorUsuarioId = registradoPorUsuarioId,
        FechaRegistro = DateTime.UtcNow,
    };

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_TraeElDocumentoConLosEventosOrdenados()
    {
        var usuarioId = await SeedUsuarioAsync();

        var doc = NuevoDocumento(usuarioId);
        doc.AgregarEvento(usuarioId, "nueva", esAutomatico: false);
        var id = await _repo.AgregarAsync(doc);
        Context.ChangeTracker.Clear();

        // Segundo evento insertado después, con Fecha más vieja: fuerza a que el orden real
        // dependa de Fecha y no del orden de inserción/Id (mismo criterio que
        // TareaRepositoryTests.ObtenerPorId_NotasOrdenadasPorFecha).
        var releido = await _repo.ObtenerPorIdAsync(id);
        releido!.AgregarEvento(usuarioId, "vieja", esAutomatico: false);
        releido.Eventos.Last().Fecha = DateTime.UtcNow.AddMinutes(-10);
        await _repo.ActualizarAsync(releido);
        Context.ChangeTracker.Clear();

        var encontrado = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(encontrado);
        Assert.Equal("0087", encontrado!.Numero);
        Assert.Equal(2026, encontrado.Anio);
        Assert.Equal(TipoDocumento.Expediente, encontrado.Tipo);
        Assert.Equal(EstadoDocumento.Pendiente, encontrado.Estado);
        Assert.Equal(2, encontrado.Eventos.Count);
        Assert.Equal("vieja", encontrado.Eventos[0].Texto);
        Assert.Equal("nueva", encontrado.Eventos[1].Texto);
    }

    [Fact]
    public async Task ObtenerPorId_Inexistente_DevuelveNull()
    {
        var encontrado = await _repo.ObtenerPorIdAsync(999);

        Assert.Null(encontrado);
    }

    [Fact]
    public async Task ObtenerPorId_TraeLaNavRegistradoPorPoblada()
    {
        // F3: ConIncludes() no traía RegistradoPor -- sin lazy loading en el proyecto (no hay
        // UseLazyLoadingProxies), la nav quedaba null y RegistradoPorNombre viajaba vacío en el
        // ADto de la Api (Task 13) aunque RegistradoPorUsuarioId estuviera bien guardado.
        var usuarioId = await SeedUsuarioAsync("registrante.conocido");
        var id = await _repo.AgregarAsync(NuevoDocumento(usuarioId));
        Context.ChangeTracker.Clear();

        var encontrado = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(encontrado!.RegistradoPor);
        Assert.Equal("registrante.conocido", encontrado.RegistradoPor!.NombreUsuario);
    }

    [Fact]
    public async Task ListarActivosAsync_SoloDevuelvePendienteYEnProceso()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0001", estado: EstadoDocumento.Pendiente));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0002", estado: EstadoDocumento.EnProceso));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0003", estado: EstadoDocumento.Finalizado));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0004", estado: EstadoDocumento.Anulado));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarActivosAsync(new FiltroDocumentos(null, null, null, null));

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, d => Assert.True(d.Estado is EstadoDocumento.Pendiente or EstadoDocumento.EnProceso));
    }

    [Fact]
    public async Task ListarCerradosAsync_SoloDevuelveFinalizadoYAnulado()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0001", estado: EstadoDocumento.Pendiente));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0002", estado: EstadoDocumento.EnProceso));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0003", estado: EstadoDocumento.Finalizado));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0004", estado: EstadoDocumento.Anulado));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarCerradosAsync(new FiltroDocumentos(null, null, null, null));

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, d => Assert.True(d.Estado is EstadoDocumento.Finalizado or EstadoDocumento.Anulado));
    }

    [Fact]
    public async Task ListarActivosAsync_FiltraPorTipo()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, tipo: TipoDocumento.Expediente, numero: "0001"));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, tipo: TipoDocumento.Oficio, numero: "0002"));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarActivosAsync(new FiltroDocumentos(TipoDocumento.Oficio, null, null, null));

        var unico = Assert.Single(resultado);
        Assert.Equal(TipoDocumento.Oficio, unico.Tipo);
    }

    [Fact]
    public async Task ListarActivosAsync_FiltraPorAnio()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, anio: 2025, numero: "0001"));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, anio: 2026, numero: "0002"));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarActivosAsync(new FiltroDocumentos(null, 2025, null, null));

        var unico = Assert.Single(resultado);
        Assert.Equal(2025, unico.Anio);
    }

    [Fact]
    public async Task ListarActivosAsync_FiltraPorTexto_BuscaEnDescripcion_CaseInsensitive()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0001", descripcion: "Poda de árbol en vereda"));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0002", descripcion: "Bacheo de calle Rivera"));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarActivosAsync(new FiltroDocumentos(null, null, "PODA", null));

        var unico = Assert.Single(resultado);
        Assert.Equal("0001", unico.Numero);
    }

    [Fact]
    public async Task ListarActivosAsync_FiltraPorTexto_BuscaTambienEnNumero_ConCoincidenciaParcial()
    {
        // I1: la UI promete "Número, descripción..." en el watermark pero el filtro solo
        // pegaba contra Descripcion -- un funcionario que tipea el número del expediente se
        // encontraba con la grilla vacía. El índice IX (Numero) ya existía sin usuario.
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0087", descripcion: "Poda de árbol en vereda"));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0099", descripcion: "Bacheo de calle Rivera"));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarActivosAsync(new FiltroDocumentos(null, null, "087", null));

        var unico = Assert.Single(resultado);
        Assert.Equal("0087", unico.Numero);
    }

    [Fact]
    public async Task ListarCerradosAsync_FiltraPorEstado_AcotaEntreFinalizadoYAnulado()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0001", estado: EstadoDocumento.Finalizado));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0002", estado: EstadoDocumento.Anulado));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarCerradosAsync(new FiltroDocumentos(null, null, null, EstadoDocumento.Finalizado));

        var unico = Assert.Single(resultado);
        Assert.Equal(EstadoDocumento.Finalizado, unico.Estado);
    }

    [Fact]
    public async Task ListarActivosAsync_CombinaVariosFiltrosALaVez()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(
            usuarioId, tipo: TipoDocumento.Expediente, anio: 2026, numero: "0001", estado: EstadoDocumento.Pendiente));
        await _repo.AgregarAsync(NuevoDocumento(
            usuarioId, tipo: TipoDocumento.Expediente, anio: 2026, numero: "0002", estado: EstadoDocumento.EnProceso));
        await _repo.AgregarAsync(NuevoDocumento(
            usuarioId, tipo: TipoDocumento.Oficio, anio: 2026, numero: "0003", estado: EstadoDocumento.Pendiente));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarActivosAsync(
            new FiltroDocumentos(TipoDocumento.Expediente, 2026, null, EstadoDocumento.Pendiente));

        var unico = Assert.Single(resultado);
        Assert.Equal("0001", unico.Numero);
    }

    [Fact]
    public async Task ListarActivosAsync_SinFiltros_DevuelveTodosLosActivos()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0001"));
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0002"));
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarActivosAsync(new FiltroDocumentos(null, null, null, null));

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public async Task ExisteNumeroAsync_ConDuplicadoExacto_DevuelveTrue()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId));
        Context.ChangeTracker.Clear();

        var existe = await _repo.ExisteNumeroAsync(TipoDocumento.Expediente, 2026, "0087");

        Assert.True(existe);
    }

    [Fact]
    public async Task ExisteNumeroAsync_ConExcluyendoIdIgualAlPropioDocumento_DevuelveFalse()
    {
        var usuarioId = await SeedUsuarioAsync();
        var id = await _repo.AgregarAsync(NuevoDocumento(usuarioId));
        Context.ChangeTracker.Clear();

        // Es exactamente lo que permite editar un documento sin chocar consigo mismo.
        var existe = await _repo.ExisteNumeroAsync(TipoDocumento.Expediente, 2026, "0087", excluyendoId: id);

        Assert.False(existe);
    }

    [Fact]
    public async Task ExisteNumeroAsync_ConExcluyendoIdDeOtroDocumento_SigueDevolviendoTrue()
    {
        var usuarioId = await SeedUsuarioAsync();
        var id = await _repo.AgregarAsync(NuevoDocumento(usuarioId));
        var otroId = await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0088"));
        Context.ChangeTracker.Clear();

        var existe = await _repo.ExisteNumeroAsync(TipoDocumento.Expediente, 2026, "0087", excluyendoId: otroId);

        Assert.True(existe);
    }

    [Fact]
    public async Task AgregarAsync_NumeroDuplicado_MapeaAReglaDeNegocio()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId));
        Context.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => _repo.AgregarAsync(NuevoDocumento(usuarioId)));

        Assert.Contains("0087", ex.Message);
        Assert.Contains("2026", ex.Message);
    }

    [Fact]
    public async Task ActualizarAsync_ChocaConOtroNumeroExistente_MapeaAReglaDeNegocio()
    {
        var usuarioId = await SeedUsuarioAsync();
        await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0087"));
        var otroId = await _repo.AgregarAsync(NuevoDocumento(usuarioId, numero: "0088"));
        Context.ChangeTracker.Clear();

        var otro = await _repo.ObtenerPorIdAsync(otroId);
        otro!.Numero = "0087"; // choca con el primero

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => _repo.ActualizarAsync(otro));
    }
}
