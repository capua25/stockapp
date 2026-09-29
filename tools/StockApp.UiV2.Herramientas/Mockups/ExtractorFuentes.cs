using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;

namespace StockApp.UiV2.Herramientas.Mockups;

/// <summary>
/// Copia a disco los TTF de Inter que embebe Avalonia.Fonts.Inter (Inter 3.19), para que los
/// mockups carguen EXACTAMENTE la misma fuente que la app, offline y sin Google Fonts.
/// Solo los pesos que usa la spec: 400, 500, 600 y 700.
/// </summary>
public static class ExtractorFuentes
{
    public static readonly IReadOnlyList<string> Pesos = ["Regular", "Medium", "SemiBold", "Bold"];

    private static readonly object Candado = new();
    private static bool _avaloniaIniciado;

    public static byte[] LeerEmbebida(string peso)
    {
        AsegurarAvalonia();
        using var origen = AssetLoader.Open(new Uri($"avares://Avalonia.Fonts.Inter/Assets/Inter-{peso}.ttf"));
        using var memoria = new MemoryStream();
        origen.CopyTo(memoria);
        return memoria.ToArray();
    }

    public static IReadOnlyList<string> Extraer(string carpeta)
    {
        Directory.CreateDirectory(carpeta);
        var escritos = new List<string>();
        foreach (var peso in Pesos)
        {
            var ruta = Path.Combine(carpeta, $"Inter-{peso}.ttf");
            File.WriteAllBytes(ruta, LeerEmbebida(peso));
            escritos.Add(ruta);
        }

        return escritos;
    }

    /// <summary>Rutas de los TTF que faltan o difieren byte a byte de los embebidos.</summary>
    public static IReadOnlyList<string> Diferencias(string carpeta) =>
        Pesos
            .Select(peso => (Ruta: Path.Combine(carpeta, $"Inter-{peso}.ttf"), Peso: peso))
            .Where(x => !File.Exists(x.Ruta)
                        || !File.ReadAllBytes(x.Ruta).AsSpan().SequenceEqual(LeerEmbebida(x.Peso)))
            .Select(x => x.Ruta)
            .ToList();

    private static void AsegurarAvalonia()
    {
        lock (Candado)
        {
            if (_avaloniaIniciado || Application.Current is not null)
            {
                _avaloniaIniciado = true;
                return;
            }

            // AssetLoader.Open fuera de una app lanza "no IAssetLoader": se inicializa la
            // plataforma headless sin arrancar ningún lifetime (verificado en consola).
            AppBuilder.Configure<Application>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
            _avaloniaIniciado = true;
        }
    }
}
