using StockApp.UiV2.Herramientas.Mockups;

namespace StockApp.UiV2.Herramientas.Tests;

/// <summary>
/// El mockup y la app tienen que dibujar con la MISMA Inter (3.19, la embebida en
/// Avalonia.Fonts.Inter 12.0.5). Si alguien reemplaza un TTF del repo por otra versión, los
/// anchos de texto cambian y la comparación visual pierde sentido.
/// </summary>
public class ExtractorFuentesTests
{
    [Fact]
    public void LeerEmbebida_DevuelveUnTrueType()
    {
        var bytes = ExtractorFuentes.LeerEmbebida("Regular");

        Assert.True(bytes.Length > 100_000);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0x00 }, bytes[..4]);
    }

    [Fact]
    public void Extraer_EscribeLosCuatroPesosIdenticosALosEmbebidos()
    {
        var carpeta = Directory.CreateTempSubdirectory("fuentes-").FullName;

        var escritos = ExtractorFuentes.Extraer(carpeta);

        Assert.Equal(4, escritos.Count);
        Assert.Empty(ExtractorFuentes.Diferencias(carpeta));
    }

    [Fact]
    public void Diferencias_ArchivoFaltante_LoReporta()
    {
        var carpeta = Directory.CreateTempSubdirectory("fuentes-").FullName;
        ExtractorFuentes.Extraer(carpeta);
        File.Delete(Path.Combine(carpeta, "Inter-Bold.ttf"));

        var diferencias = ExtractorFuentes.Diferencias(carpeta);

        Assert.Equal("Inter-Bold.ttf", Path.GetFileName(Assert.Single(diferencias)));
    }

    [Fact]
    public void FuentesDelRepo_SonByteIdenticasALasEmbebidas() =>
        Assert.Empty(ExtractorFuentes.Diferencias(RutasRepo.CarpetaFuentes));
}
