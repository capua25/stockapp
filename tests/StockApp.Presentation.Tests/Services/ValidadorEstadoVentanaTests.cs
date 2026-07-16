using System.Collections.Generic;
using Avalonia;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Verifica la lógica pura que decide si un <see cref="EstadoVentana"/> guardado sigue
/// siendo visible en la configuración de pantallas actual (caso: monitor desenchufado).
/// </summary>
public class ValidadorEstadoVentanaTests
{
    private static readonly PixelRect PantallaPrincipal = new(0, 0, 1920, 1080);

    [Fact]
    public void EsVisibleEn_VentanaDentroDeLaPantalla_DevuelveTrue()
    {
        var estado = new EstadoVentana { Ancho = 800, Alto = 600, X = 100, Y = 100 };

        var resultado = ValidadorEstadoVentana.EsVisibleEn(estado, new[] { PantallaPrincipal });

        Assert.True(resultado);
    }

    [Fact]
    public void EsVisibleEn_VentanaParcialmenteFueraDeLaPantalla_DevuelveTrue()
    {
        // Intersecta aunque una parte quede fuera del área visible.
        var estado = new EstadoVentana { Ancho = 800, Alto = 600, X = 1800, Y = 100 };

        var resultado = ValidadorEstadoVentana.EsVisibleEn(estado, new[] { PantallaPrincipal });

        Assert.True(resultado);
    }

    [Fact]
    public void EsVisibleEn_VentanaCompletamenteFueraDeTodasLasPantallas_DevuelveFalse()
    {
        // Simula el monitor secundario desenchufado: la ventana estaba ahí y ya no hay
        // ninguna pantalla que la contenga.
        var estado = new EstadoVentana { Ancho = 800, Alto = 600, X = 5000, Y = 5000 };

        var resultado = ValidadorEstadoVentana.EsVisibleEn(estado, new[] { PantallaPrincipal });

        Assert.False(resultado);
    }

    [Fact]
    public void EsVisibleEn_SinPantallas_DevuelveFalse()
    {
        var estado = new EstadoVentana { Ancho = 800, Alto = 600, X = 100, Y = 100 };

        var resultado = ValidadorEstadoVentana.EsVisibleEn(estado, System.Array.Empty<PixelRect>());

        Assert.False(resultado);
    }

    [Fact]
    public void EsVisibleEn_VisibleEnUnaDeVariasPantallas_DevuelveTrue()
    {
        var pantallaSecundaria = new PixelRect(1920, 0, 1920, 1080);
        var estado = new EstadoVentana { Ancho = 800, Alto = 600, X = 2000, Y = 100 };

        var pantallas = new List<PixelRect> { PantallaPrincipal, pantallaSecundaria };
        var resultado = ValidadorEstadoVentana.EsVisibleEn(estado, pantallas);

        Assert.True(resultado);
    }
}
