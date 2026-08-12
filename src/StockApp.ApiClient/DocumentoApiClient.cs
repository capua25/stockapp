using System.Globalization;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using StockApp.Application.Documentos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient;

internal sealed record EventoDocumentoWire(
    int Id, int UsuarioId, DateTime Fecha,
    EstadoDocumento? EstadoAnterior, EstadoDocumento? EstadoNuevo,
    string Texto, bool EsAutomatico);

internal sealed record DocumentoWire(
    int Id, string Numero, int Anio, TipoDocumento Tipo,
    DateTime FechaEmision, string Descripcion, EstadoDocumento Estado,
    int RegistradoPorUsuarioId, string? RegistradoPorNombre,
    DateTime FechaRegistro, DateTime? FechaCierre,
    bool EsActivo, bool EsCerrado,
    List<EventoDocumentoWire> Eventos);

internal sealed record CrearDocumentoBody(string Numero, int Anio, TipoDocumento Tipo, DateTime FechaEmision, string Descripcion);
internal sealed record EditarDocumentoBody(string Numero, int Anio, TipoDocumento Tipo, DateTime FechaEmision, string Descripcion);
internal sealed record AgregarNotaDocumentoBody(string Texto);
internal sealed record MotivoBody(string Motivo);

/// <summary>IDocumentoAdministrativoService contra /documentos.</summary>
public sealed class DocumentoApiClient : IDocumentoAdministrativoService
{
    /// <summary>
    /// Motivos/descripciones libres pueden traer acentos ("desistió"). Sin esto,
    /// PostAsJsonAsync usa el encoder HTML-safe por defecto y escapa a \uXXXX: el server los
    /// decodifica igual, pero rompe cualquier assert que compare el JSON crudo (Task 16).
    /// </summary>
    private static readonly JsonSerializerOptions JsonBody = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HttpClient _http;

    public DocumentoApiClient(HttpClient http) => _http = http;

    public async Task<int> RegistrarAsync(DocumentoAdministrativo documento)
    {
        var body = new CrearDocumentoBody(
            documento.Numero, documento.Anio, documento.Tipo, documento.FechaEmision, documento.Descripcion);
        var response = await ApiErrores.EnviarAsync(() => _http.PostAsJsonAsync("documentos", body, JsonBody));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al registrar el documento.");
        return creado.Id;
    }

    public async Task EditarAsync(int id, DatosEdicionDocumento datos)
    {
        var body = new EditarDocumentoBody(datos.Numero, datos.Anio, datos.Tipo, datos.FechaEmision, datos.Descripcion);
        var response = await ApiErrores.EnviarAsync(() => _http.PutAsJsonAsync($"documentos/{id}", body, JsonBody));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarActivosAsync(FiltroDocumentos filtro)
        => ListarAsync("documentos/activos", filtro, incluirEstado: false);

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarHistorialAsync(FiltroDocumentos filtro)
        => ListarAsync("documentos/historial", filtro, incluirEstado: true);

    private async Task<IReadOnlyList<DocumentoAdministrativo>> ListarAsync(
        string ruta, FiltroDocumentos filtro, bool incluirEstado)
    {
        var query = ApiQuery.Construir(
            ("tipo", filtro.Tipo is null ? null : ((int)filtro.Tipo.Value).ToString(CultureInfo.InvariantCulture)),
            ("anio", filtro.Anio?.ToString(CultureInfo.InvariantCulture)),
            ("texto", filtro.Texto),
            ("estado", !incluirEstado || filtro.Estado is null
                ? null : ((int)filtro.Estado.Value).ToString(CultureInfo.InvariantCulture)));

        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta + query));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<DocumentoWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    public async Task<DocumentoAdministrativo?> ObtenerPorIdAsync(int id)
    {
        try
        {
            var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"documentos/{id}"));
            await ApiErrores.AsegurarExitoAsync(response);

            var dto = await response.Content.ReadFromJsonAsync<DocumentoWire>();
            return dto is null ? null : AEntidad(dto);
        }
        catch (EntidadNoEncontradaException)
        {
            return null;  // 404 = documento inexistente: contrato de la interfaz (null)
        }
    }

    public Task IniciarProcesoAsync(int id) => PostSinBodyAsync($"documentos/{id}/iniciar");
    public Task VolverAPendienteAsync(int id) => PostSinBodyAsync($"documentos/{id}/volver-a-pendiente");
    public Task FinalizarAsync(int id) => PostSinBodyAsync($"documentos/{id}/finalizar");

    public async Task AgregarNotaAsync(int id, string texto)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"documentos/{id}/notas", new AgregarNotaDocumentoBody(texto), JsonBody));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task AnularAsync(int id, string motivo)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"documentos/{id}/anular", new MotivoBody(motivo), JsonBody));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task ReabrirAsync(int id, string motivo)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"documentos/{id}/reabrir", new MotivoBody(motivo), JsonBody));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    private async Task PostSinBodyAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.PostAsync(ruta, content: null));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    private static DocumentoAdministrativo AEntidad(DocumentoWire dto) => new()
    {
        Id = dto.Id,
        Numero = dto.Numero,
        Anio = dto.Anio,
        Tipo = dto.Tipo,
        FechaEmision = dto.FechaEmision,
        Descripcion = dto.Descripcion,
        Estado = dto.Estado,
        RegistradoPorUsuarioId = dto.RegistradoPorUsuarioId,
        RegistradoPor = dto.RegistradoPorNombre is null
            ? null : new Usuario { Id = dto.RegistradoPorUsuarioId, NombreUsuario = dto.RegistradoPorNombre },
        FechaRegistro = dto.FechaRegistro,
        FechaCierre = dto.FechaCierre,
        Eventos = dto.Eventos.Select(e => new EventoDocumento
        {
            Id = e.Id, DocumentoAdministrativoId = dto.Id, UsuarioId = e.UsuarioId, Fecha = e.Fecha,
            EstadoAnterior = e.EstadoAnterior, EstadoNuevo = e.EstadoNuevo,
            Texto = e.Texto, EsAutomatico = e.EsAutomatico,
        }).ToList(),
    };
}
