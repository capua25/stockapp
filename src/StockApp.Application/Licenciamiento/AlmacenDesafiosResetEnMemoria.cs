using System.Security.Cryptography;

namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Implementación en memoria del custodio de desafíos. Thread-safe con lock. TTL por defecto
/// 24 h. El reloj es inyectable para testear la expiración sin esperas reales.
/// </summary>
public sealed class AlmacenDesafiosResetEnMemoria : IAlmacenDesafiosReset
{
    private readonly object _lock = new();
    private readonly TimeSpan _ttl;
    private readonly Func<DateTime> _ahora;

    private string? _desafio;
    private DateTime _expira;

    public AlmacenDesafiosResetEnMemoria(TimeSpan? ttl = null, Func<DateTime>? ahora = null)
    {
        _ttl   = ttl ?? TimeSpan.FromHours(24);
        _ahora = ahora ?? (() => DateTime.UtcNow);
    }

    public string GenerarNuevo()
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (_lock)
        {
            _desafio = nonce;
            _expira  = _ahora() + _ttl;
        }
        return nonce;
    }

    public ResultadoDesafio Consumir(string desafio)
    {
        lock (_lock)
        {
            if (_desafio is null || _desafio != desafio)
                return ResultadoDesafio.Inexistente;

            if (_ahora() > _expira)
            {
                _desafio = null;
                return ResultadoDesafio.Expirado;
            }

            _desafio = null; // un solo uso
            return ResultadoDesafio.Valido;
        }
    }
}
