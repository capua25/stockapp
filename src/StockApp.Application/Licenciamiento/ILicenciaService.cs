namespace StockApp.Application.Licenciamiento;

/// <summary>Estado de licencia visto por el desktop (pantalla de bloqueo).</summary>
public record EstadoLicenciaDto(bool Activada, string CodigoMaquina);

/// <summary>Resultado de intentar activar una licencia desde el desktop.</summary>
public record ResultadoActivacionDto(bool Exito, string? Motivo);

/// <summary>Consulta y activación de licencia contra la API (endpoints /licencia/*).</summary>
public interface ILicenciaService
{
    Task<EstadoLicenciaDto> ObtenerEstadoAsync();
    Task<ResultadoActivacionDto> ActivarAsync(string licencia);
}
