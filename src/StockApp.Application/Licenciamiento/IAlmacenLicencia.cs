namespace StockApp.Application.Licenciamiento;

/// <summary>Persistencia de la licencia activa (un solo string firmado).</summary>
public interface IAlmacenLicencia
{
    /// <summary>El string de licencia persistido, o null si no hay ninguno.</summary>
    Task<string?> LeerAsync();

    /// <summary>Persiste (sobrescribe) el string de licencia.</summary>
    Task GuardarAsync(string licencia);
}
