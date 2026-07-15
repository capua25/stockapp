namespace StockApp.Application.Licenciamiento;

/// <summary>Desafío de reset devuelto por la API (nonce + código de máquina para copiar).</summary>
public record DesafioResetDto(string Desafio, string CodigoMaquina);

/// <summary>Resultado de aplicar un reset de Admin desde el desktop.</summary>
public record ResultadoResetDto(bool Exito, string? Motivo);

/// <summary>Flujo de recuperación de Admin contra la API (endpoints /auth/reset-admin/*).</summary>
public interface IResetAdminService
{
    Task<DesafioResetDto> SolicitarDesafioAsync();
    Task<ResultadoResetDto> ResetearAsync(string token, string nuevaContrasena);
}
