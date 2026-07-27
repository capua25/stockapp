using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Backups;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Administracion;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Administracion;

public class MantenimientoViewModelTests
{
    private static (MantenimientoViewModel vm,
                    Mock<IBackupsService> backupsMock,
                    Mock<IServicioGuardadoArchivo> guardadoMock,
                    Mock<IConfirmacionService> confirmacionMock)
        Crear(IReadOnlyList<CorridaBackupDto>? corridas = null)
    {
        var backupsMock = new Mock<IBackupsService>();
        backupsMock.Setup(b => b.ListarAsync(It.IsAny<CancellationToken>())).ReturnsAsync(corridas ?? new List<CorridaBackupDto>());

        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var confirmacionMock = new Mock<IConfirmacionService>();

        var vm = new MantenimientoViewModel(backupsMock.Object, guardadoMock.Object, confirmacionMock.Object);
        return (vm, backupsMock, guardadoMock, confirmacionMock);
    }

    [Fact]
    public async Task CargarAsync_PopulaCorridas()
    {
        var (vm, _, _, _) = Crear(new List<CorridaBackupDto>
        {
            new(1, new DateTime(2026, 7, 27, 3, 0, 0, DateTimeKind.Utc), "Exitosa", "backup_1.dump", 1024, null),
            new(2, new DateTime(2026, 7, 26, 15, 0, 0, DateTimeKind.Utc), "Fallida", null, null, "pg_dump falló"),
        });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Corridas.Count);
        Assert.Equal("Exitosa", vm.Corridas[0].Resultado);
        Assert.Equal(1, vm.Corridas[0].Id);
        Assert.False(vm.Corridas[0].Descargando);
    }

    [Fact]
    public async Task CargarAsync_MientrasCarga_CargandoEsTrueYLuegoFalse()
    {
        var (vm, _, _, _) = Crear();

        var tarea = vm.CargarAsync();
        await tarea;

        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task CargarAsync_ElServicioFalla_InformaElErrorYNoRompe()
    {
        var backupsMock = new Mock<IBackupsService>();
        backupsMock.Setup(b => b.ListarAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("servidor caído"));
        var confirmacionMock = new Mock<IConfirmacionService>();
        var vm = new MantenimientoViewModel(backupsMock.Object, new Mock<IServicioGuardadoArchivo>().Object, confirmacionMock.Object);

        await vm.CargarAsync();

        confirmacionMock.Verify(c => c.InformarAsync("servidor caído"), Times.Once);
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task DescargarCommand_CopiaElStreamAlServicioDeGuardadoConElNombreCorrecto()
    {
        var (vm, backupsMock, guardadoMock, _) = Crear();
        var fila = new FilaCorridaBackupVm(new CorridaBackupDto(5, DateTime.UtcNow, "Exitosa", "backup_5.dump", 2048, null));
        var streamFake = new MemoryStream(new byte[] { 1, 2, 3 });
        backupsMock.Setup(b => b.DescargarAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupDescargaDto("backup_5.dump", streamFake));
        guardadoMock.Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "backup_5.dump", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await vm.DescargarCommand.ExecuteAsync(fila);

        guardadoMock.Verify(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "backup_5.dump", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(fila.Descargando);
    }

    [Fact]
    public async Task DescargarCommand_ElServicioFalla_InformaElErrorYNoRompe()
    {
        var (vm, backupsMock, _, confirmacionMock) = Crear();
        var fila = new FilaCorridaBackupVm(new CorridaBackupDto(5, DateTime.UtcNow, "Exitosa", "backup_5.dump", 2048, null));
        backupsMock.Setup(b => b.DescargarAsync(5, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("archivo no disponible"));

        await vm.DescargarCommand.ExecuteAsync(fila);

        confirmacionMock.Verify(c => c.InformarAsync("archivo no disponible"), Times.Once);
        Assert.False(fila.Descargando);
    }

    [Fact]
    public async Task DescargarCommand_UsuarioCancelaElSelector_NoInformaError()
    {
        var (vm, backupsMock, guardadoMock, confirmacionMock) = Crear();
        var fila = new FilaCorridaBackupVm(new CorridaBackupDto(5, DateTime.UtcNow, "Exitosa", "backup_5.dump", 2048, null));
        backupsMock.Setup(b => b.DescargarAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupDescargaDto("backup_5.dump", new MemoryStream()));
        guardadoMock.Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await vm.DescargarCommand.ExecuteAsync(fila);

        confirmacionMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DescargarCommand_MientrasDescarga_FilaQuedaEnDescargando()
    {
        var (vm, backupsMock, guardadoMock, _) = Crear();
        var fila = new FilaCorridaBackupVm(new CorridaBackupDto(5, DateTime.UtcNow, "Exitosa", "backup_5.dump", 2048, null));
        var tcsIniciada = new TaskCompletionSource();
        var tcsDescarga = new TaskCompletionSource<BackupDescargaDto>();
        backupsMock.Setup(b => b.DescargarAsync(5, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                tcsIniciada.SetResult();
                // No completa sola: obliga al comando a seguir "en curso" hasta que el test
                // libere tcsDescarga, igual que el patrón usado en CancelarCommand_* más abajo.
                return await tcsDescarga.Task;
            });
        guardadoMock.Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var tarea = vm.DescargarCommand.ExecuteAsync(fila);
        await tcsIniciada.Task;

        Assert.True(fila.Descargando);

        tcsDescarga.SetResult(new BackupDescargaDto("backup_5.dump", new MemoryStream()));
        await tarea;
        Assert.False(fila.Descargando);
    }

    [Fact]
    public async Task CancelarCommand_CancelaElTokenDeLaDescargaEnCurso_DejaLaFilaConsistente()
    {
        var (vm, backupsMock, _, confirmacionMock) = Crear();
        var fila = new FilaCorridaBackupVm(new CorridaBackupDto(5, DateTime.UtcNow, "Exitosa", "backup_5.dump", 2048, null));
        var tcsIniciada = new TaskCompletionSource();
        var tcsNuncaCompleta = new TaskCompletionSource<BackupDescargaDto>();
        backupsMock.Setup(b => b.DescargarAsync(5, It.IsAny<CancellationToken>()))
            .Returns(async (int id, CancellationToken ct) =>
            {
                tcsIniciada.SetResult();
                // Simula el servidor colgado: nunca completa por su cuenta, solo el ct lo corta.
                return await tcsNuncaCompleta.Task.WaitAsync(ct);
            });

        var tareaDescarga = vm.DescargarCommand.ExecuteAsync(fila);
        await tcsIniciada.Task;
        Assert.True(fila.Descargando);

        vm.CancelarCommand.Execute(fila);
        await tareaDescarga;

        // Estado consistente: no queda "descargando" para siempre, y la cancelación deliberada
        // no se reporta como error (requisito explícito de la corrección).
        Assert.False(fila.Descargando);
        Assert.Null(fila.Cts);
        confirmacionMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CancelarCommand_SinDescargaEnCurso_NoLanza()
    {
        var (vm, _, _, _) = Crear();
        var fila = new FilaCorridaBackupVm(new CorridaBackupDto(5, DateTime.UtcNow, "Exitosa", "backup_5.dump", 2048, null));

        vm.CancelarCommand.Execute(fila);

        Assert.False(fila.Descargando);
    }
}
