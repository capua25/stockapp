using StockApp.Application.Interfaces;

namespace StockApp.Infrastructure.Auth;

/// <summary>
/// Implementación de <see cref="IPasswordHasher"/> usando BCrypt.Net-Next.
/// Work factor 12 (≈250 ms en hardware moderno; ajustar si cambia el hardware).
/// </summary>
public class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string plaintext)
        => BCrypt.Net.BCrypt.HashPassword(plaintext, WorkFactor);

    public bool Verify(string plaintext, string hash)
        => BCrypt.Net.BCrypt.Verify(plaintext, hash);
}
