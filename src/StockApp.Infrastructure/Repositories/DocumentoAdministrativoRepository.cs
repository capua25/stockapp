using Microsoft.EntityFrameworkCore;
using Npgsql;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class DocumentoAdministrativoRepository : IDocumentoAdministrativoRepository
{
    private readonly AppDbContext _ctx;

    public DocumentoAdministrativoRepository(AppDbContext ctx) => _ctx = ctx;

    private IQueryable<DocumentoAdministrativo> ConIncludes() =>
        _ctx.DocumentosAdministrativos
            .Include(d => d.Eventos.OrderBy(e => e.Fecha).ThenBy(e => e.Id))
            .Include(d => d.RegistradoPor);

    public async Task<int> AgregarAsync(DocumentoAdministrativo documento)
    {
        try
        {
            _ctx.DocumentosAdministrativos.Add(documento);
            await _ctx.SaveChangesAsync();
            return documento.Id;
        }
        catch (DbUpdateException ex) when (EsViolacionNumeroUnico(ex))
        {
            throw new ReglaDeNegocioException(
                $"Ya existe un {documento.Tipo} {documento.Numero}/{documento.Anio}.");
        }
    }

    public Task<DocumentoAdministrativo?> ObtenerPorIdAsync(int id)
        => ConIncludes().FirstOrDefaultAsync(d => d.Id == id);

    private static readonly EstadoDocumento[] EstadosActivos = { EstadoDocumento.Pendiente, EstadoDocumento.EnProceso };
    private static readonly EstadoDocumento[] EstadosCerrados = { EstadoDocumento.Finalizado, EstadoDocumento.Anulado };

    // El filtrado Activos/Historial va en el SQL (WHERE Estado IN (...)), nunca en memoria: si
    // se trajera todo con un ListarAsync genérico y se filtrara en el servicio, un historial con
    // miles de filas arrastraría el archivo completo de la base para descartar la mitad en el
    // cliente — exactamente el problema que D9 evita no paginando.
    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarActivosAsync(FiltroDocumentos filtro)
        => ListarConFiltroAsync(filtro, EstadosActivos);

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarCerradosAsync(FiltroDocumentos filtro)
        => ListarConFiltroAsync(filtro, EstadosCerrados);

    private async Task<IReadOnlyList<DocumentoAdministrativo>> ListarConFiltroAsync(
        FiltroDocumentos filtro, EstadoDocumento[] estadosPermitidos)
    {
        var query = ConIncludes().Where(d => estadosPermitidos.Contains(d.Estado));

        if (filtro.Tipo is not null)
            query = query.Where(d => d.Tipo == filtro.Tipo);
        if (filtro.Anio is not null)
            query = query.Where(d => d.Anio == filtro.Anio);
        if (filtro.Estado is not null)
            query = query.Where(d => d.Estado == filtro.Estado);
        if (!string.IsNullOrWhiteSpace(filtro.Texto))
            query = query.Where(d =>
                EF.Functions.ILike(d.Descripcion, $"%{filtro.Texto}%") ||
                EF.Functions.ILike(d.Numero, $"%{filtro.Texto}%"));

        return await query
            .OrderByDescending(d => d.FechaRegistro)
            .ThenByDescending(d => d.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Eventos nuevos (Id == 0, agregados por el servicio a la colección de un documento ya
    /// tracked): se agregan EXPLÍCITAMENTE al DbSet en vez de confiar en el fixup automático
    /// del change tracker — mismo criterio explícito que TareaRepository.ActualizarAsync con
    /// NotaTarea.
    /// </summary>
    public async Task ActualizarAsync(DocumentoAdministrativo documento)
    {
        try
        {
            foreach (var evento in documento.Eventos.Where(e => e.Id == 0))
            {
                evento.DocumentoAdministrativoId = documento.Id;
                _ctx.EventosDocumento.Add(evento);
            }

            _ctx.DocumentosAdministrativos.Update(documento);
            await _ctx.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (EsViolacionNumeroUnico(ex))
        {
            throw new ReglaDeNegocioException(
                $"Ya existe un {documento.Tipo} {documento.Numero}/{documento.Anio}.");
        }
    }

    public Task<bool> ExisteNumeroAsync(TipoDocumento tipo, int anio, string numero, int? excluyendoId = null)
    {
        var query = _ctx.DocumentosAdministrativos
            .Where(d => d.Tipo == tipo && d.Anio == anio && d.Numero == numero);

        if (excluyendoId is not null)
            query = query.Where(d => d.Id != excluyendoId);

        return query.AnyAsync();
    }

    /// <summary>
    /// Mismo patrón que GastoRepository.EsViolacionFacturaUnica: el índice único
    /// IX_DocumentosAdministrativos_Tipo_Anio_Numero (AppDbContext, Task 4) es la última
    /// defensa contra dos funcionarios cargando el mismo expediente a la vez. Sin este catch
    /// acá, la violación llegaría como DbUpdateException cruda y el endpoint respondería 500
    /// en vez de 409.
    /// </summary>
    private static bool EsViolacionNumeroUnico(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
           && pg.ConstraintName == "IX_DocumentosAdministrativos_Tipo_Anio_Numero";
}
