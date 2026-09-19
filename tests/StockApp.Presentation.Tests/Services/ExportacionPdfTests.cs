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
}
