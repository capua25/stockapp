namespace StockApp.Application.Auth;

/// <summary>
/// Reglas mínimas de contraseña aplicadas en todo el sistema.
/// Centralizado para evitar duplicación entre AltaUsuario, CambiarContrasena y PrimerArranque.
/// </summary>
internal static class ContrasenaValidator
{
    private const int LongitudMinima = 6;

    /// <summary>
    /// Lanza <see cref="ArgumentException"/> si la contraseña es null, vacía/whitespace o
    /// tiene menos de <see cref="LongitudMinima"/> caracteres.
    /// </summary>
    public static void Validar(string? contrasena)
    {
        if (string.IsNullOrWhiteSpace(contrasena))
            throw new ArgumentException("La contraseña no puede estar vacía.");

        if (contrasena.Length < LongitudMinima)
            throw new ArgumentException(
                $"La contraseña debe tener al menos {LongitudMinima} caracteres.");
    }
}
