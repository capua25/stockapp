namespace StockApp.UiV2.Herramientas;

/// <summary>Rutas del repositorio, resueltas subiendo desde el binario hasta encontrar StockApp.sln.</summary>
public static class RutasRepo
{
    public static string Raiz { get; } = BuscarRaiz(AppContext.BaseDirectory);

    public static string TokensJson => Path.Combine(Raiz, "docs", "ui-v2", "tokens.json");

    public static string TokensAxaml =>
        Path.Combine(Raiz, "src", "StockApp.Presentation", "Themes", "V2", "Tokens.axaml");

    public static string CarpetaMockups => Path.Combine(Raiz, "docs", "ui-v2", "mockups");

    public static string CarpetaBaseMockups => Path.Combine(CarpetaMockups, "_base");

    public static string TokensCss => Path.Combine(CarpetaBaseMockups, "tokens.css");

    public static string CarpetaFuentes => Path.Combine(CarpetaBaseMockups, "fuentes");

    internal static string BuscarRaiz(string desde)
    {
        for (var dir = new DirectoryInfo(desde); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "StockApp.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"No se encontró StockApp.sln subiendo desde {desde}.");
    }
}
