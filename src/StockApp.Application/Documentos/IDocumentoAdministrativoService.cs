using StockApp.Domain.Entities;

namespace StockApp.Application.Documentos;

/// <summary>
/// Documentos administrativos (expedientes, oficios, suministros) — spec 2026-08-11. Molde
/// exacto de ITareaService: métodos por acción, cada uno con su propio permiso, su propia
/// validación y su propia línea de auditoría — nunca un CambiarEstado genérico.
/// </summary>
public interface IDocumentoAdministrativoService
{
    /// <summary>Alta. RegistradoPorUsuarioId y FechaRegistro se completan SIEMPRE desde la
    /// sesión, nunca desde <paramref name="documento"/>. Valida número único (Tipo, Anio,
    /// Numero) y siembra el evento inicial automático.</summary>
    Task<int> RegistrarAsync(DocumentoAdministrativo documento);

    /// <summary>Corrige Numero/Anio/Tipo/FechaEmision/Descripcion sobre un documento activo
    /// (D1). Revalida el número único si cambia la clave. Implementado en Task 11.</summary>
    Task EditarAsync(int id, DatosEdicionDocumento datos);

    /// <summary>Documentos con EsActivo (Pendiente/EnProceso). Solapa Activos.</summary>
    Task<IReadOnlyList<DocumentoAdministrativo>> ListarActivosAsync(FiltroDocumentos filtro);

    /// <summary>Documentos con EsCerrado (Finalizado/Anulado). Exige Anio no nulo en el
    /// filtro (ArgumentException, 400) — D9. Solapa Historial, carga perezosa.</summary>
    Task<IReadOnlyList<DocumentoAdministrativo>> ListarHistorialAsync(FiltroDocumentos filtro);

    Task<DocumentoAdministrativo?> ObtenerPorIdAsync(int id);

    /// <summary>Pendiente → EnProceso. Rechaza con ReglaDeNegocioException si el documento no
    /// está Pendiente — guarda de estado origen simétrica a la de ReabrirAsync (D4/D8): en el
    /// dominio, Finalizado/Anulado → EnProceso también son válidas (son la reapertura), así
    /// que sin esta guarda esta acción podría reabrir un documento cerrado sin pasar por
    /// documentos.administrar. Implementado en Task 8.</summary>
    Task IniciarProcesoAsync(int id);

    /// <summary>EnProceso → Pendiente. Análogo exacto de TareaService.SoltarAsync. Implementado en Task 8.</summary>
    Task VolverAPendienteAsync(int id);

    /// <summary>EnProceso → Finalizado. Sella FechaCierre. Implementado en Task 8.</summary>
    Task FinalizarAsync(int id);

    /// <summary>Nota manual, append-only. Implementado en Task 9.</summary>
    Task AgregarNotaAsync(int id, string texto);

    /// <summary>Cualquier estado activo → Anulado. Solo Admin, motivo obligatorio, sella
    /// FechaCierre. Implementado en Task 10.</summary>
    Task AnularAsync(int id, string motivo);

    /// <summary>Finalizado/Anulado → EnProceso. Solo Admin, motivo obligatorio, exige que el
    /// documento esté EsCerrado, limpia FechaCierre. Implementado en Task 10.</summary>
    Task ReabrirAsync(int id, string motivo);
}
