namespace StockApp.UiV2.Herramientas.Tokens;

/// <summary>tokens.json no cumple las reglas de la spec (formato de color, base 4, pesos, nombres).</summary>
public sealed class TokensInvalidosException(string mensaje) : Exception(mensaje);

/// <summary>
/// Rol tipográfico. AltoLinea null = alto natural de la fuente, que Avalonia redondea hacia arriba:
/// ceil(Tamano × FactorAltoLinea). El CSS de los mockups emite ese mismo valor.
/// </summary>
public sealed record EstiloTipografico(double Tamano, int Peso, double? AltoLinea, double? EspaciadoLetras, bool Mayusculas);

/// <summary>La única sombra del sistema (spec §2.2). Color #RRGGBB + opacidad 0..1.</summary>
public sealed record SombraToken(double X, double Y, double Desenfoque, double Expansion, string Color, double Opacidad);

/// <summary>
/// tokens.json ya validado. Las listas conservan el orden del documento: el orden de los
/// archivos generados es estable y un diff de tokens.json se lee igual en su salida.
/// FactorAltoLinea: (ascender + descender) / unitsPerEm de la Inter embebida (3.19).
/// </summary>
public sealed record DefinicionTokens(
    string Prefijo,
    double FactorAltoLinea,
    IReadOnlyList<(string Nombre, string Hex)> Colores,
    IReadOnlyList<(string Nombre, double Valor)> Radios,
    IReadOnlyList<(string Nombre, double Valor)> Alturas,
    IReadOnlyList<(string Nombre, double Valor)> Anchos,
    IReadOnlyList<double> Espaciado,
    SombraToken SombraCard,
    IReadOnlyList<(string Nombre, EstiloTipografico Estilo)> Tipografia);
