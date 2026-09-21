using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Guardián de compatibilidad (spec 2026-09-18, export PDF): GuardarBytesAsync ganó dos
/// parámetros nuevos (extension, tipoMime) para que el flujo de PDF pueda pedir un filtro de
/// archivo en el selector nativo de guardado. Los call sites existentes (backups, logs, en
/// MantenimientoViewModel) siguen llamando a la firma de 2/3 argumentos sin tocarse -- este
/// test confirma que, sin pasar extension ni tipoMime, ambos resuelven a null exactamente como
/// antes (ningún DefaultExtension ni FileTypeChoices en el selector).
/// </summary>
public class ServicioGuardadoArchivoCompatibilidadTests
{
    [Fact]
    public async Task GuardarBytesAsync_LlamadoSinExtensionNiTipoMime_ResuelveAmbosComoNull()
    {
        string? extensionRecibida = "no-deberia-quedar-asi";
        string? tipoMimeRecibido = "no-deberia-quedar-asi";

        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<Stream, string, CancellationToken, string?, string?>(
                (_, _, _, extension, tipoMime) =>
                {
                    extensionRecibida = extension;
                    tipoMimeRecibido = tipoMime;
                })
            .ReturnsAsync(true);

        // Llamada "vieja", tal cual la hace MantenimientoViewModel.DescargarLogsAsync: solo
        // stream + nombre, sin extension ni tipoMime. El binding es contra el TIPO de interfaz
        // (guardadoMock.Object es IServicioGuardadoArchivo), así que resuelve los defaults
        // declarados en la interfaz, no en una implementación concreta.
        await guardadoMock.Object.GuardarBytesAsync(new MemoryStream(), "backup_5.dump");

        Assert.Null(extensionRecibida);
        Assert.Null(tipoMimeRecibido);
    }
}
