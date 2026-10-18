using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Application.Documentos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Fake mínimo de IDocumentoAdministrativoService (mismo criterio que TareaServiceFake: este
/// proyecto no referencia Moq). Cuenta las llamadas a ListarHistorialAsync para verificar la
/// carga perezosa del historial (D9) contra la vista real.
/// </summary>
internal sealed class DocumentoServiceFake : IDocumentoAdministrativoService
{
    private readonly List<DocumentoAdministrativo> _activos;
    private readonly List<DocumentoAdministrativo> _historial;

    public DocumentoServiceFake(
        List<DocumentoAdministrativo>? activos = null, List<DocumentoAdministrativo>? historial = null)
    {
        _activos = activos ?? new List<DocumentoAdministrativo>();
        _historial = historial ?? new List<DocumentoAdministrativo>();
    }

    public int LlamadasListarHistorial { get; private set; }
    public int LlamadasListarActivos { get; private set; }
    public FiltroDocumentos? UltimoFiltroActivos { get; private set; }

    public Task<int> RegistrarAsync(DocumentoAdministrativo documento)
    {
        documento.Id = _activos.Count + _historial.Count + 1;
        _activos.Add(documento);
        return Task.FromResult(documento.Id);
    }

    public Task EditarAsync(int id, DatosEdicionDocumento datos) => Task.CompletedTask;

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarActivosAsync(FiltroDocumentos filtro)
    {
        LlamadasListarActivos++;
        UltimoFiltroActivos = filtro;
        return Task.FromResult<IReadOnlyList<DocumentoAdministrativo>>(_activos.ToList());
    }

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarHistorialAsync(FiltroDocumentos filtro)
    {
        LlamadasListarHistorial++;
        return Task.FromResult<IReadOnlyList<DocumentoAdministrativo>>(_historial.ToList());
    }

    public Task<DocumentoAdministrativo?> ObtenerPorIdAsync(int id) =>
        Task.FromResult<DocumentoAdministrativo?>(_activos.Concat(_historial).First(d => d.Id == id));

    public Task IniciarProcesoAsync(int id)
    {
        _activos.First(d => d.Id == id).CambiarEstado(EstadoDocumento.EnProceso);
        return Task.CompletedTask;
    }

    public Task VolverAPendienteAsync(int id)
    {
        _activos.First(d => d.Id == id).CambiarEstado(EstadoDocumento.Pendiente);
        return Task.CompletedTask;
    }

    public Task FinalizarAsync(int id)
    {
        _activos.First(d => d.Id == id).CambiarEstado(EstadoDocumento.Finalizado);
        return Task.CompletedTask;
    }

    public Task AgregarNotaAsync(int id, string texto) => Task.CompletedTask;

    public Task AnularAsync(int id, string motivo)
    {
        _activos.First(d => d.Id == id).CambiarEstado(EstadoDocumento.Anulado);
        return Task.CompletedTask;
    }

    public Task ReabrirAsync(int id, string motivo)
    {
        _historial.First(d => d.Id == id).CambiarEstado(EstadoDocumento.EnProceso);
        return Task.CompletedTask;
    }
}

/// <summary>Fake mínimo de IAdjuntoDocumentoService: sin adjuntos por defecto, no se ejercita
/// en los recorridos de DocumentoListViewTests.</summary>
internal sealed class AdjuntoDocumentoServiceFake : IAdjuntoDocumentoService
{
    public Task<AdjuntoDocumentoDto> AgregarAsync(int documentoId, string nombreArchivo, byte[] contenido) =>
        throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task<IReadOnlyList<AdjuntoDocumentoDto>> ListarPorDocumentoAsync(int documentoId) =>
        Task.FromResult<IReadOnlyList<AdjuntoDocumentoDto>>(Array.Empty<AdjuntoDocumentoDto>());

    public Task<AdjuntoDocumentoContenidoDto> ObtenerContenidoAsync(int adjuntoId) =>
        throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task QuitarAsync(int adjuntoId) =>
        throw new NotSupportedException("No usado en este banco de pruebas.");
}
