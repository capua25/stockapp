using System;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Cubre la lógica pura de <see cref="ServicioAperturaArchivo.SanitizarYValidarExtension"/>:
/// sanitización de path traversal y validación de extensión contra la whitelist de
/// AdjuntoValidador. Lo que toca el filesystem/Process.Start real no se testea (plataforma).
/// </summary>
public class ServicioAperturaArchivoTests
{
    [Theory]
    [InlineData("../../etc/x.pdf", ".pdf")]
    [InlineData("..\\..\\x.jpg", ".jpg")]
    [InlineData("/etc/passwd.png", ".png")]
    public void SanitizarYValidarExtension_ConPathTraversal_IgnoraElPathYValidaSoloLaExtension(
        string nombreArchivo, string extensionEsperada)
    {
        var extension = ServicioAperturaArchivo.SanitizarYValidarExtension(nombreArchivo);

        Assert.Equal(extensionEsperada, extension);
    }

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("app.desktop")]
    [InlineData("pagina.html")]
    public void SanitizarYValidarExtension_ConExtensionNoPermitida_Lanza(string nombreArchivo)
    {
        Assert.Throws<InvalidOperationException>(
            () => ServicioAperturaArchivo.SanitizarYValidarExtension(nombreArchivo));
    }

    [Theory]
    [InlineData("factura.pdf", ".pdf")]
    [InlineData("foto.jpg", ".jpg")]
    [InlineData("foto.JPEG", ".jpeg")]
    [InlineData("captura.PNG", ".png")]
    public void SanitizarYValidarExtension_ConExtensionValida_DevuelveExtensionEnMinusculas(
        string nombreArchivo, string extensionEsperada)
    {
        var extension = ServicioAperturaArchivo.SanitizarYValidarExtension(nombreArchivo);

        Assert.Equal(extensionEsperada, extension);
    }
}
