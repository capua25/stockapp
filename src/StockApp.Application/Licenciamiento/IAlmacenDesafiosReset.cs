namespace StockApp.Application.Licenciamiento;

/// <summary>Resultado de consumir un desafío de reset.</summary>
public enum ResultadoDesafio { Valido, Inexistente, Expirado }

/// <summary>
/// Custodia el nonce de reset: uno solo activo a la vez, con TTL, de un solo uso.
/// En memoria (se pierde al reiniciar la API, que es justo lo que se quiere: no persiste desafíos).
/// </summary>
public interface IAlmacenDesafiosReset
{
    /// <summary>Genera un nonce nuevo e invalida cualquiera anterior. Devuelve el nonce.</summary>
    string GenerarNuevo();

    /// <summary>Consume el nonce si está vivo (lo elimina). Informa si no existe o expiró.</summary>
    ResultadoDesafio Consumir(string desafio);
}
