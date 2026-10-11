using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>True si el importe no es cero: la Diferencia del pie de Ingreso por factura pasa a advertencia.</summary>
public static class DistintoDeCeroConverter
{
    public static readonly FuncValueConverter<decimal, bool> Instance = new(valor => valor != 0m);
}
