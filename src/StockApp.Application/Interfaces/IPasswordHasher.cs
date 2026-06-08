namespace StockApp.Application.Interfaces;

/// <summary>
/// Abstracción de hashing de contraseñas. Application no conoce el algoritmo concreto.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Calcula el hash con sal de <paramref name="plaintext"/>.</summary>
    string Hash(string plaintext);

    /// <summary>Verifica que <paramref name="plaintext"/> corresponde a <paramref name="hash"/>.</summary>
    bool Verify(string plaintext, string hash);
}
