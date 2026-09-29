using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Los tokens v2 se resuelven desde la app (DynamicResource falla en silencio si la clave no
/// existe) y conviven con los viejos sin pisarlos.
/// </summary>
public class TokensV2Tests
{
    private static object Recurso(string clave)
    {
        Assert.True(
            Avalonia.Application.Current!.TryGetResource(clave, ThemeVariant.Light, out var valor),
            $"El token '{clave}' no se resuelve: Themes/V2/Tokens.axaml no está incluido o la clave cambió.");
        return valor!;
    }

    [AvaloniaTheory]
    [InlineData("V2FondoBrush", "#F7F7F5")]
    [InlineData("V2AcentoBrush", "#1D4ED8")]
    [InlineData("V2BordeInputBrush", "#D6D3D1")]
    [InlineData("V2InsigniaExitoTextoBrush", "#14532D")]
    [InlineData("V2AlertaInfoTextoBrush", "#1E3A8A")]
    public void BrushV2_SeResuelveConElValorDeLaSpec(string clave, string hex) =>
        Assert.Equal(Color.Parse(hex), Assert.IsAssignableFrom<ISolidColorBrush>(Recurso(clave)).Color);

    [AvaloniaFact]
    public void MedidasV2_SeResuelvenConSuTipo()
    {
        Assert.Equal(36.0, Assert.IsType<double>(Recurso("V2AltoControl")));
        Assert.Equal(new CornerRadius(10), Assert.IsType<CornerRadius>(Recurso("V2RadioCard")));
        Assert.Equal(BoxShadows.Parse("0 1 2 0 #0D000000"), Assert.IsType<BoxShadows>(Recurso("V2SombraCard")));
        Assert.Equal(FontWeight.SemiBold, Assert.IsType<FontWeight>(Recurso("V2FuenteTituloPaginaPeso")));
        Assert.Equal(26.0, Assert.IsType<double>(Recurso("V2FuenteTituloPaginaAltoLinea")));
    }

    [AvaloniaTheory]
    [InlineData("BrandBrush", "#16A34A")]
    [InlineData("FondoBrush", "#F8FAFC")]
    [InlineData("BordeBrush", "#E2E8F0")]
    public void TokensViejos_SiguenIntactos(string clave, string hex) =>
        Assert.Equal(Color.Parse(hex), Assert.IsAssignableFrom<ISolidColorBrush>(Recurso(clave)).Color);
}
