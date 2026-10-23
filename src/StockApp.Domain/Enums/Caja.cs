namespace StockApp.Domain.Enums;

/// <summary>
/// Caja municipal. Dos cajas con saldos independientes y sin transferencias entre ellas.
/// Se persiste como int y viaja como número en la API: no reordenar ni insertar miembros.
/// </summary>
public enum Caja
{
    Local = 0,
    Poa = 1,
}
