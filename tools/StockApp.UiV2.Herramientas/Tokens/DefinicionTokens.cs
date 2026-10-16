namespace StockApp.UiV2.Herramientas.Tokens;

/// <summary>tokens.json no cumple las reglas de la spec (formato de color, base 4, pesos, nombres).</summary>
public sealed class TokensInvalidosException(string mensaje) : Exception(mensaje);

/// <summary>
/// Rol tipográfico. AltoLinea null = alto natural de la fuente: en una sola línea Avalonia lo redondea hacia arriba,
/// ceil(Tamano × FactorAltoLinea), y el CSS de los mockups emite ese valor. En texto de varias líneas eso NO vale:
/// el alto natural fraccionario (p. ej. 16,94 a 14 px) alterna entre dos valores enteros y desalinea las líneas contra
/// el mockup. Por eso Control y Cuerpo fijan AltoLinea y su estilo asigna LineHeight; los roles que siguen con null
/// tienen el mismo defecto latente si algún día envuelven en varias líneas.
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
