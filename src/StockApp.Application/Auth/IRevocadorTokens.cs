namespace StockApp.Application.Auth;

/// <summary>
/// Revocación de JWTs (hardening post-Fase B). Guarda por usuario el mínimo instante de
/// emisión (iat) aceptado: cualquier token emitido ANTES de ese instante deja de ser
/// válido, aunque no haya vencido por tiempo. Se usa tras resetear la contraseña de un
/// usuario (mutuo o firmado) para invalidar sesiones viejas de inmediato.
/// </summary>
public interface IRevocadorTokens
{
    /// <summary>Invalida todo token de <paramref name="usuarioId"/> emitido antes de <paramref name="ahora"/>.</summary>
    void Revocar(int usuarioId, DateTime ahora);

    /// <summary>Indica si un token con claim iat = <paramref name="emitidoEn"/> sigue siendo válido.</summary>
    bool EsValido(int usuarioId, DateTime emitidoEn);
}
