using StockApp.Domain.Enums;

namespace StockApp.Application.Auth;

/// <summary>
/// Valida que un <see cref="RolUsuario"/> recibido desde afuera sea uno de los valores
/// definidos del enum. Centralizado como hermano de <see cref="ContrasenaValidator"/> y
/// <see cref="NombreUsuarioValidator"/>: sin un <c>JsonStringEnumConverter</c> configurado,
/// un valor fuera de rango (ej. <c>{"rol":99}</c>) deserializa igual — no falla en el
/// binding — y un chequeo de solo-null (como el que hacían los endpoints) lo deja pasar.
/// </summary>
internal static class RolUsuarioValidator
{
    /// <summary>
    /// Lanza <see cref="ArgumentException"/> si <paramref name="rol"/> no es un valor
    /// definido de <see cref="RolUsuario"/>.
    /// </summary>
    public static void ValidarDefinido(RolUsuario rol)
    {
        if (!Enum.IsDefined(typeof(RolUsuario), rol))
            throw new ArgumentException($"El rol '{rol}' no es un valor válido.");
    }
}
