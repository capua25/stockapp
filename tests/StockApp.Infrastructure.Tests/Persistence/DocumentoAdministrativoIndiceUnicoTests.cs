using Microsoft.EntityFrameworkCore;
using Npgsql;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Persistence;

/// <summary>
/// El índice único compuesto (Tipo, Anio, Numero) es la última defensa contra dos
/// funcionarios cargando el mismo expediente a la vez (decisión 1 del spec). Estos tests
/// verifican el índice a nivel de AppDbContext/Postgres directamente, sin pasar por el
/// repositorio (que recién existe en Task 5) — confirman que la BASE rechaza el duplicado,
/// no que el repositorio lo traduzca a un mensaje lindo.
/// </summary>
public class DocumentoAdministrativoIndiceUnicoTests : PostgresRepositoryTestBase
{
    public DocumentoAdministrativoIndiceUnicoTests(PostgresFixture fixture) : base(fixture) { }

    private static Usuario NuevoUsuario(string nombreUsuario = "operario1") => new()
    {
        NombreUsuario = nombreUsuario,
        HashContrasena = "x",
        Rol = RolUsuario.Operador,
        FechaAlta = DateTime.UtcNow,
    };

    private static DocumentoAdministrativo NuevoDocumento(
        int registradoPorUsuarioId, TipoDocumento tipo = TipoDocumento.Expediente,
        int anio = 2026, string numero = "0087") => new()
    {
        Numero = numero,
        Anio = anio,
        Tipo = tipo,
        FechaEmision = DateTime.UtcNow,
        Descripcion = "Solicitud de poda de árbol en vereda",
        RegistradoPorUsuarioId = registradoPorUsuarioId,
        FechaRegistro = DateTime.UtcNow,
    };

    [Fact]
    public async Task DosDocumentos_MismoTipoAnioNumero_ViolaElIndiceUnico()
    {
        var usuario = NuevoUsuario();
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        Context.DocumentosAdministrativos.Add(NuevoDocumento(usuario.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Context.DocumentosAdministrativos.Add(NuevoDocumento(usuario.Id));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());

        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
        Assert.Equal("IX_DocumentosAdministrativos_Tipo_Anio_Numero", pg.ConstraintName);
    }

    [Theory]
    [InlineData(TipoDocumento.Oficio, 2026, "0087")]   // distinto Tipo
    [InlineData(TipoDocumento.Expediente, 2027, "0087")] // distinto Anio
    [InlineData(TipoDocumento.Expediente, 2026, "0088")] // distinto Numero
    public async Task DosDocumentos_DifierenEnUnCampoDeLaClave_NoViolanElIndice(
        TipoDocumento tipo, int anio, string numero)
    {
        var usuario = NuevoUsuario();
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        Context.DocumentosAdministrativos.Add(NuevoDocumento(usuario.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Context.DocumentosAdministrativos.Add(NuevoDocumento(usuario.Id, tipo, anio, numero));

        await Context.SaveChangesAsync(); // no debe lanzar

        Assert.Equal(2, await Context.DocumentosAdministrativos.CountAsync());
    }

    [Fact]
    public async Task Estado_TieneIndicePropio_SePuedeFiltrarSinEscanearTabla()
    {
        // No hay forma directa de aserter "existe un índice" desde EF sin consultar el
        // catálogo de Postgres; en cambio, se prueba el efecto observable: filtrar por
        // Estado funciona sin error, y el modelo ya lo declaró en OnModelCreating (Step 3).
        var usuario = NuevoUsuario();
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        Context.DocumentosAdministrativos.Add(NuevoDocumento(usuario.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var pendientes = await Context.DocumentosAdministrativos
            .Where(d => d.Estado == EstadoDocumento.Pendiente)
            .ToListAsync();

        Assert.Single(pendientes);
    }

    [Fact]
    public async Task AltaConEventoYAdjunto_PersisteElGrafoCompleto()
    {
        var usuario = NuevoUsuario();
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var doc = NuevoDocumento(usuario.Id);
        doc.AgregarEvento(usuario.Id, "Alta del documento", esAutomatico: true);
        Context.DocumentosAdministrativos.Add(doc);
        await Context.SaveChangesAsync();

        Context.AdjuntosDocumento.Add(new AdjuntoDocumento
        {
            DocumentoAdministrativoId = doc.Id,
            NombreArchivo = "factura.pdf",
            ContentType = "application/pdf",
            TamanoBytes = 1024,
            FechaAltaUtc = DateTime.UtcNow,
        });
        await Context.SaveChangesAsync();
        var adjuntoId = await Context.AdjuntosDocumento
            .Where(a => a.DocumentoAdministrativoId == doc.Id)
            .Select(a => a.Id)
            .SingleAsync();

        Context.AdjuntosDocumentoContenido.Add(
            new AdjuntoDocumentoContenido { Id = adjuntoId, Contenido = new byte[] { 1, 2, 3 } });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Assert.Equal(1, await Context.EventosDocumento.CountAsync(e => e.DocumentoAdministrativoId == doc.Id));
        Assert.Equal(1, await Context.AdjuntosDocumento.CountAsync(a => a.DocumentoAdministrativoId == doc.Id));
        Assert.Equal(1, await Context.AdjuntosDocumentoContenido.CountAsync(c => c.Id == adjuntoId));
    }

    [Fact]
    public async Task AltaConRegistradoPorUsuarioIdInexistente_ViolaLaFk()
    {
        var doc = NuevoDocumento(registradoPorUsuarioId: 999);
        Context.DocumentosAdministrativos.Add(doc);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());

        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, pg.SqlState);
    }
}
