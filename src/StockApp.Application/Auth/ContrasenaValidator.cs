namespace StockApp.Application.Auth;

/// <summary>
/// Reglas mínimas de contraseña aplicadas en todo el sistema.
/// Centralizado para evitar duplicación entre AltaUsuario, CambiarContrasena y PrimerArranque.
/// </summary>
internal static class ContrasenaValidator
{
    private const int LongitudMinima = 8;

    /// <summary>
    /// Lanza <see cref="ArgumentException"/> si la contraseña es null, vacía/whitespace,
    /// tiene menos de <see cref="LongitudMinima"/> caracteres, o no incluye al menos una
    /// letra y al menos un número.
    /// </summary>
    public static void Validar(string? contrasena)
    {
        if (string.IsNullOrWhiteSpace(contrasena))
            throw new ArgumentException("La contraseña no puede estar vacía.");

        // Un solo mensaje para todos los casos de incumplimiento (largo, sin letra, sin
        // número): no hace falta distinguir la causa y evita filtrar qué le falta.
        if (contrasena.Length < LongitudMinima
            || !contrasena.Any(char.IsLetter)
            || !contrasena.Any(char.IsDigit))
        {
            throw new ArgumentException(
                $"La contraseña debe tener al menos {LongitudMinima} caracteres e incluir al menos una letra y un número.");
        }
    }
}
