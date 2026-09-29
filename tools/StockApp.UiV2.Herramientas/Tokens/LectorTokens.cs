using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StockApp.UiV2.Herramientas.Tokens;

/// <summary>
/// Lee y valida docs/ui-v2/tokens.json. Valida lo que la spec fija como regla (§2.2: base 4;
/// colores sólidos #RRGGBB; pesos 400 a 700) para que un token mal escrito falle acá y no
/// como un color raro en una pantalla.
/// </summary>
public static partial class LectorTokens
{
    private static readonly int[] PesosValidos = [400, 500, 600, 700];

    [GeneratedRegex("^#[0-9A-F]{6}$")]
    private static partial Regex HexValido();

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex NombreValido();

    [GeneratedRegex("^([A-Z][A-Za-z0-9]*)?$")]
    private static partial Regex PrefijoValido();

    public static DefinicionTokens LeerArchivo(string ruta) => Leer(File.ReadAllText(ruta));

    public static DefinicionTokens Leer(string json)
    {
        using var documento = JsonDocument.Parse(json);
        var raiz = documento.RootElement;

        var prefijo = Texto(raiz, "prefijo");
        if (!PrefijoValido().IsMatch(prefijo))
        {
            throw new TokensInvalidosException($"prefijo '{prefijo}' inválido: vacío o PascalCase.");
        }

        var factorAltoLinea = Numero(raiz, "factorAltoLinea");
        if (factorAltoLinea is < 1 or > 2)
        {
            throw new TokensInvalidosException(
                $"factorAltoLinea: {factorAltoLinea.ToString(CultureInfo.InvariantCulture)} fuera de [1, 2].");
        }

        var colores = new List<(string, string)>();
        foreach (var seccion in new[] { "colores", "coloresDerivados" })
        {
            foreach (var p in Objeto(raiz, seccion).EnumerateObject())
            {
                var hex = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : "";
                if (!HexValido().IsMatch(hex))
                {
                    throw new TokensInvalidosException(
                        $"{seccion}.{p.Name}: '{hex}' no es un color #RRGGBB en mayúsculas.");
                }

                colores.Add((ValidarNombre(seccion, p.Name), hex));
            }
        }

        ValidarUnicos("colores", colores.Select(c => c.Item1));

        var espaciado = new List<double>();
        foreach (var e in Propiedad(raiz, "espaciado").EnumerateArray())
        {
            var v = e.GetDouble();
            if (v <= 0 || v % 4 != 0)
            {
                throw new TokensInvalidosException(
                    $"espaciado: {v.ToString(CultureInfo.InvariantCulture)} no es múltiplo de 4 (spec §2.2, base 4).");
            }

            espaciado.Add(v);
        }

        var s = Objeto(raiz, "sombraCard");
        var sombra = new SombraToken(
            Numero(s, "x"), Numero(s, "y"), Numero(s, "desenfoque"), Numero(s, "expansion"),
            Texto(s, "color"), Numero(s, "opacidad"));
        if (!HexValido().IsMatch(sombra.Color))
        {
            throw new TokensInvalidosException($"sombraCard.color: '{sombra.Color}' no es #RRGGBB.");
        }

        if (sombra.Opacidad is < 0 or > 1)
        {
            throw new TokensInvalidosException(
                $"sombraCard.opacidad: {sombra.Opacidad.ToString(CultureInfo.InvariantCulture)} fuera de [0, 1].");
        }

        var tipografia = new List<(string, EstiloTipografico)>();
        foreach (var p in Objeto(raiz, "tipografia").EnumerateObject())
        {
            var t = p.Value;
            var peso = (int)Numero(t, "peso");
            if (!PesosValidos.Contains(peso))
            {
                throw new TokensInvalidosException($"tipografia.{p.Name}.peso: {peso} no es 400, 500, 600 ni 700.");
            }

            tipografia.Add((ValidarNombre("tipografia", p.Name), new EstiloTipografico(
                Numero(t, "tamano"),
                peso,
                t.TryGetProperty("altoLinea", out var alto) ? alto.GetDouble() : null,
                t.TryGetProperty("espaciadoLetras", out var espaciadoLetras) ? espaciadoLetras.GetDouble() : null,
                t.TryGetProperty("mayusculas", out var mayusculas) && mayusculas.GetBoolean())));
        }

        ValidarUnicos("tipografia", tipografia.Select(t => t.Item1));

        return new DefinicionTokens(
            prefijo,
            factorAltoLinea,
            colores,
            Numeros(raiz, "radios"),
            Numeros(raiz, "alturas"),
            Numeros(raiz, "anchos"),
            espaciado,
            sombra,
            tipografia);
    }

    private static List<(string, double)> Numeros(JsonElement raiz, string seccion)
    {
        var lista = new List<(string, double)>();
        foreach (var p in Objeto(raiz, seccion).EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Number || p.Value.GetDouble() <= 0)
            {
                throw new TokensInvalidosException($"{seccion}.{p.Name}: debe ser un número positivo.");
            }

            lista.Add((ValidarNombre(seccion, p.Name), p.Value.GetDouble()));
        }

        ValidarUnicos(seccion, lista.Select(x => x.Item1));
        return lista;
    }

    private static JsonElement Propiedad(JsonElement elemento, string nombre) =>
        elemento.TryGetProperty(nombre, out var valor)
            ? valor
            : throw new TokensInvalidosException($"falta la sección '{nombre}'.");

    private static JsonElement Objeto(JsonElement elemento, string nombre)
    {
        var valor = Propiedad(elemento, nombre);
        return valor.ValueKind == JsonValueKind.Object
            ? valor
            : throw new TokensInvalidosException($"'{nombre}' debe ser un objeto.");
    }

    private static string Texto(JsonElement elemento, string nombre)
    {
        var valor = Propiedad(elemento, nombre);
        return valor.ValueKind == JsonValueKind.String
            ? valor.GetString()!
            : throw new TokensInvalidosException($"'{nombre}' debe ser texto.");
    }

    private static double Numero(JsonElement elemento, string nombre)
    {
        var valor = Propiedad(elemento, nombre);
        return valor.ValueKind == JsonValueKind.Number
            ? valor.GetDouble()
            : throw new TokensInvalidosException($"'{nombre}' debe ser un número.");
    }

    private static string ValidarNombre(string seccion, string nombre) =>
        NombreValido().IsMatch(nombre)
            ? nombre
            : throw new TokensInvalidosException($"{seccion}.{nombre}: el nombre debe ser PascalCase sin guiones.");

    private static void ValidarUnicos(string seccion, IEnumerable<string> nombres)
    {
        var duplicado = nombres.GroupBy(n => n).FirstOrDefault(g => g.Count() > 1);
        if (duplicado is not null)
        {
            throw new TokensInvalidosException($"{seccion}: nombre duplicado '{duplicado.Key}'.");
        }
    }
}
