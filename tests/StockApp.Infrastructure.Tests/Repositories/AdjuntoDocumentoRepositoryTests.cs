using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class AdjuntoDocumentoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly AdjuntoDocumentoRepository _repo;
    private readonly DocumentoAdministrativoRepository _repoDocumento;

    public AdjuntoDocumentoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new AdjuntoDocumentoRepository(Context);
        _repoDocumento = new DocumentoAdministrativoRepository(Context);
    }

    private async Task<int> SeedDocumentoAsync()
    {
        var usuario = new Usuario
        {
            NombreUsuario = "operario1",
            HashContrasena = "x",
            Rol = RolUsuario.Operador,
            FechaAlta = DateTime.UtcNow,
        };
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var doc = new DocumentoAdministrativo
        {
            Numero = "0087",
            Anio = 2026,
            Tipo = TipoDocumento.Expediente,
            FechaEmision = DateTime.UtcNow,
            Descripcion = "Solicitud de poda de árbol en vereda",
            RegistradoPorUsuarioId = usuario.Id,
            FechaRegistro = DateTime.UtcNow,
        };
        return await _repoDocumento.AgregarAsync(doc);
    }

    private static AdjuntoDocumento NuevoAdjunto(int documentoId, string nombreArchivo = "factura.pdf") => new()
    {
        DocumentoAdministrativoId = documentoId,
        NombreArchivo = nombreArchivo,
        ContentType = "application/pdf",
        TamanoBytes = 3,
        FechaAltaUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task AgregarAsync_PersisteMetadatosYContenidoPorSeparado()
    {
        var documentoId = await SeedDocumentoAsync();
        Context.ChangeTracker.Clear();

        var id = await _repo.AgregarAsync(NuevoAdjunto(documentoId), new byte[] { 1, 2, 3 });
        Context.ChangeTracker.Clear();

        var metadatos = await _repo.ObtenerPorIdAsync(id);
        Assert.NotNull(metadatos);
        Assert.Equal("factura.pdf", metadatos!.NombreArchivo);
        Assert.True(metadatos.Activo);

        var contenido = await _repo.ObtenerContenidoAsync(id);
        Assert.Equal(new byte[] { 1, 2, 3 }, contenido);
    }

    [Fact]
    public async Task ListarPorDocumentoAsync_NoArrastraLosBytes()
    {
        var documentoId = await SeedDocumentoAsync();
        Context.ChangeTracker.Clear();

        await _repo.AgregarAsync(NuevoAdjunto(documentoId, "factura.pdf"), new byte[] { 1, 2, 3 });
        await _repo.AgregarAsync(NuevoAdjunto(documentoId, "nota.jpg"), new byte[] { 4, 5, 6 });
        Context.ChangeTracker.Clear();

        var listado = await _repo.ListarPorDocumentoAsync(documentoId);

        Assert.Equal(2, listado.Count);
        Assert.All(listado, a => Assert.Equal(documentoId, a.DocumentoAdministrativoId));
    }

    [Fact]
    public async Task ObtenerPorIdAsync_Inexistente_DevuelveNull()
    {
        var encontrado = await _repo.ObtenerPorIdAsync(999);

        Assert.Null(encontrado);
    }

    [Fact]
    public async Task ObtenerContenidoAsync_Inexistente_DevuelveNull()
    {
        var contenido = await _repo.ObtenerContenidoAsync(999);

        Assert.Null(contenido);
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_PersisteActivoEnFalse()
    {
        var documentoId = await SeedDocumentoAsync();
        Context.ChangeTracker.Clear();

        var id = await _repo.AgregarAsync(NuevoAdjunto(documentoId), new byte[] { 1, 2, 3 });
        Context.ChangeTracker.Clear();

        var adjunto = await _repo.ObtenerPorIdAsync(id);
        adjunto!.Activo = false;
        await _repo.ActualizarAsync(adjunto);
        Context.ChangeTracker.Clear();

        var releido = await _repo.ObtenerPorIdAsync(id);
        Assert.False(releido!.Activo);
    }
}
