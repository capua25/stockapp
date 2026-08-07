namespace StockApp.Application.Auth;

/// <summary>
/// Reglas mínimas de <c>NombreUsuario</c> aplicadas en el alta. Centralizado como hermano
/// de <see cref="ContrasenaValidator"/> para evitar duplicación y para que el límite de
/// longitud se mantenga sincronizado con el <c>HasMaxLength(100)</c> de <c>AppDbContext</c>.
/// </summary>
internal static class NombreUsuarioValidator
{
    private const int LongitudMaxima = 100;

    /// <summary>
    /// Recorta espacios al borde y valida. Devuelve el nombre ya trimeado, listo para
    /// persistir — lanza <see cref="ArgumentException"/> si queda vacío o si supera
    /// <see cref="LongitudMaxima"/> caracteres.
    /// Deuda deliberada: NO normaliza mayúsculas/minúsculas. <c>AuthService.BuscarPorNombreAsync</c>
    /// compara el nombre con <c>==</c> exacto en el login; normalizar el case acá sin tocar
    /// esa comparación rompería el login de cualquier usuario ya creado con case mixto.
    /// Si se decide normalizar, hay que revisar el login en el mismo cambio.
    /// </summary>
    public static string ValidarYNormalizar(string? nombreUsuario)
    {
        if (string.IsNullOrWhiteSpace(nombreUsuario))
            throw new ArgumentException("El nombre de usuario no puede estar vacío.");

        var trimeado = nombreUsuario.Trim();

        if (trimeado.Length > LongitudMaxima)
            throw new ArgumentException(
                $"El nombre de usuario no puede superar los {LongitudMaxima} caracteres.");

        return trimeado;
    }
}
