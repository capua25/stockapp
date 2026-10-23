namespace StockApp.Domain.Enums;

/// <summary>Texto visible de la caja (único lugar: combos, grillas, PDF, mensajes).</summary>
public static class CajaExtensions
{
    public static IReadOnlyList<Caja> Todas { get; } = new[] { Caja.Local, Caja.Poa };

    public static string Nombre(this Caja caja) => caja switch
    {
        Caja.Local => "Local",
        Caja.Poa => "POA",
        _ => ((int)caja).ToString(),
    };
}
