using System;
using System.Threading.Tasks;
using Moq;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Análogo a ExportacionCsv (bugfix 2026-08-14): centraliza el try/catch de guardado a disco
/// para que un fallo DESPUÉS de elegir la ubicación (permiso denegado, disco lleno) se informe
/// en vez de escapar del comando sin observar.
/// </summary>
public class ExportacionPdfTests
{
    [Fact]
    public async Task EjecutarAsync_CaminoFeliz_NoInformaNada()
    {
        var confirmMock = new Mock<IConfirmacionService>();

        await ExportacionPdf.EjecutarAsync(() => Task.CompletedTask, confirmMock.Object);

        confirmMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EjecutarAsync_SiLaOperacionFalla_InformaYNoPropagaLaExcepcion()
    {
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        await ExportacionPdf.EjecutarAsync(
            () => throw new System.IO.IOException("disco lleno"), confirmMock.Object);

        confirmMock.Verify(c => c.InformarAsync("No se pudo guardar el archivo. disco lleno"), Times.Once);
    }

    [Fact]
    public async Task OfrecerAbrirAsync_SiElUsuarioConfirma_AbreElArchivo()
    {
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        var aperturaMock = new Mock<IServicioAperturaArchivo>();
        var contenido = new byte[] { 1, 2, 3 };

        await ExportacionPdf.OfrecerAbrirAsync(contenido, "valorizacion.pdf", confirmMock.Object, aperturaMock.Object);

        aperturaMock.Verify(a => a.AbrirAsync("valorizacion.pdf", contenido), Times.Once);
    }

    [Fact]
    public async Task OfrecerAbrirAsync_SiElUsuarioCancela_NoAbreNada()
    {
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);
        var aperturaMock = new Mock<IServicioAperturaArchivo>();

        await ExportacionPdf.OfrecerAbrirAsync(new byte[] { 1 }, "valorizacion.pdf", confirmMock.Object, aperturaMock.Object);

        aperturaMock.Verify(a => a.AbrirAsync(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task OfrecerAbrirAsync_SiFallaLaApertura_NoPropagaYElMensajeNoDiceQueFalloLaExportacion()
    {
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var aperturaMock = new Mock<IServicioAperturaArchivo>();
        aperturaMock
            .Setup(a => a.AbrirAsync(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ThrowsAsync(new InvalidOperationException("no hay visor de PDF asociado"));

        await ExportacionPdf.OfrecerAbrirAsync(new byte[] { 1 }, "valorizacion.pdf", confirmMock.Object, aperturaMock.Object);

        confirmMock.Verify(
            c => c.InformarAsync(It.Is<string>(m =>
                m.Contains("no se pudo abrir", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("no se pudo guardar", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("no se pudo exportar", StringComparison.OrdinalIgnoreCase))),
            Times.Once);
    }
}
