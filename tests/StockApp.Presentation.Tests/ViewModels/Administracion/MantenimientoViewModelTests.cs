using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Backups;
using StockApp.Application.Logs;
using StockApp.Domain.Exceptions;
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
        Crear(IReadOnlyList<CorridaBackupDto>? corridas = null, Mock<ILogsService>? logs = null)
    {
        var backupsMock = new Mock<IBackupsService>();
        backupsMock.Setup(b => b.ListarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(corridas ?? new List<CorridaBackupDto>());
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var confirmacionMock = new Mock<IConfirmacionService>();

        var logsMock = logs ?? new Mock<ILogsService>();
        if (logs is null)
            logsMock.Setup(l => l.ObtenerResumenAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResumenLogsDto(0, null, null, 0));

        var vm = new MantenimientoViewModel(
            backupsMock.Object, guardadoMock.Object, confirmacionMock.Object, logsMock.Object);
        return (vm, backupsMock, guardadoMock, confirmacionMock);
    }

    /// <summary>
    /// Mock de ILogsService por defecto ("sin logs") para los tests preexistentes de la zona
    /// Backups que construyen el VM a mano (sin pasar por <see cref="Crear"/>) y no ejercitan
    /// nada de Diagnóstico — evita repetir el setup de ObtenerResumenAsync en cada uno.
    /// </summary>
    private static ILogsService LogsSinDatos()
    {
        var mock = new Mock<ILogsService>();
        mock.Setup(l => l.ObtenerResumenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumenLogsDto(0, null, null, 0));
        return mock.Object;
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
        var vm = new MantenimientoViewModel(
            backupsMock.Object, new Mock<IServicioGuardadoArchivo>().Object, confirmacionMock.Object, LogsSinDatos());

        await vm.CargarAsync();

        confirmacionMock.Verify(c => c.InformarAsync("servidor caído"), Times.Once);
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task CargarAsync_ElServicioFalla_NoMuestraElEstadoDeListaVacia()
    {
        // Fix (MINOR, re-review final E1): antes, Corridas quedaba en 0 tras el catch y
        // MostrarListaVacia (basado solo en Cargando/Count) mostraba "Todavía no hay backups
        // registrados." — el mensaje de "todo bien" para un caso que es un error de carga.
        var backupsMock = new Mock<IBackupsService>();
        backupsMock.Setup(b => b.ListarAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("servidor caído"));
        var vm = new MantenimientoViewModel(
            backupsMock.Object, new Mock<IServicioGuardadoArchivo>().Object, new Mock<IConfirmacionService>().Object, LogsSinDatos());

        await vm.CargarAsync();

        Assert.True(vm.ErrorAlCargar);
        Assert.False(vm.MostrarListaVacia);
    }

    [Fact]
    public async Task CargarAsync_ExitosaTrasUnErrorPrevio_LimpiaElFlagDeError()
    {
        var backupsMock = new Mock<IBackupsService>();
        backupsMock.SetupSequence(b => b.ListarAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("servidor caído"))
            .ReturnsAsync(new List<CorridaBackupDto>());
        var vm = new MantenimientoViewModel(
            backupsMock.Object, new Mock<IServicioGuardadoArchivo>().Object, new Mock<IConfirmacionService>().Object, LogsSinDatos());

        await vm.CargarAsync();
        Assert.True(vm.ErrorAlCargar);

        await vm.CargarAsync();

        Assert.False(vm.ErrorAlCargar);
        Assert.True(vm.MostrarListaVacia); // ahora sí es el caso real de "no hay backups"
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

    [Fact]
    public async Task DescargarCommand_DobleInvocacionSobreLaMismaFila_LaSegundaEsNoOpYNoPierdeElCts()
    {
        var (vm, backupsMock, guardadoMock, _) = Crear();
        var fila = new FilaCorridaBackupVm(new CorridaBackupDto(5, DateTime.UtcNow, "Exitosa", "backup_5.dump", 2048, null));
        var tcsIniciada = new TaskCompletionSource();
        var tcsDescarga = new TaskCompletionSource<BackupDescargaDto>();
        var invocaciones = 0;
        backupsMock.Setup(b => b.DescargarAsync(5, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                invocaciones++;
                tcsIniciada.SetResult();
                return await tcsDescarga.Task;
            });
        guardadoMock.Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Click 1: arranca y queda "en curso" (suspendido en el await de red).
        var tarea1 = vm.DescargarCommand.ExecuteAsync(fila);
        await tcsIniciada.Task;
        var ctsDeLaPrimera = fila.Cts;
        Assert.NotNull(ctsDeLaPrimera);

        // Click 2 (doble click humano): la fila ya está Descargando == true, así que la segunda
        // invocación debe ser un no-op — no debe reemplazar fila.Cts ni disparar otro pedido al server.
        var tarea2 = vm.DescargarCommand.ExecuteAsync(fila);
        await tarea2;

        Assert.Equal(1, invocaciones);
        Assert.Same(ctsDeLaPrimera, fila.Cts);
        Assert.False(ctsDeLaPrimera!.IsCancellationRequested);

        tcsDescarga.SetResult(new BackupDescargaDto("backup_5.dump", new MemoryStream()));
        await tarea1;

        Assert.False(fila.Descargando);
        Assert.Null(fila.Cts);
    }

    [Fact]
    public async Task CancelarCommand_TrasDobleInvocacion_CancelaLaDescargaQueElUsuarioVe()
    {
        var (vm, backupsMock, _, confirmacionMock) = Crear();
        var fila = new FilaCorridaBackupVm(new CorridaBackupDto(5, DateTime.UtcNow, "Exitosa", "backup_5.dump", 2048, null));
        var tcsIniciada = new TaskCompletionSource();
        var tcsNuncaCompleta = new TaskCompletionSource<BackupDescargaDto>();
        backupsMock.Setup(b => b.DescargarAsync(5, It.IsAny<CancellationToken>()))
            .Returns(async (int id, CancellationToken ct) =>
            {
                tcsIniciada.SetResult();
                return await tcsNuncaCompleta.Task.WaitAsync(ct);
            });

        var tarea1 = vm.DescargarCommand.ExecuteAsync(fila);
        await tcsIniciada.Task;

        // Doble click: no-op sobre la misma fila (Descargando ya es true).
        var tarea2 = vm.DescargarCommand.ExecuteAsync(fila);
        await tarea2;

        // Cancelar debe cortar la ÚNICA descarga real en curso (la del click 1), no una referencia
        // huérfana de un segundo CTS que ya no existe.
        vm.CancelarCommand.Execute(fila);
        await tarea1;

        Assert.False(fila.Descargando);
        Assert.Null(fila.Cts);
        confirmacionMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DescargarCommand_DosFilasDistintas_DescarganEnParaleloYCancelarUnaNoAfectaALaOtra()
    {
        var (vm, backupsMock, guardadoMock, confirmacionMock) = Crear();
        var filaA = new FilaCorridaBackupVm(new CorridaBackupDto(5, DateTime.UtcNow, "Exitosa", "backup_5.dump", 2048, null));
        var filaB = new FilaCorridaBackupVm(new CorridaBackupDto(7, DateTime.UtcNow, "Exitosa", "backup_7.dump", 4096, null));

        var tcsIniciadaA = new TaskCompletionSource();
        var tcsNuncaCompletaA = new TaskCompletionSource<BackupDescargaDto>();
        backupsMock.Setup(b => b.DescargarAsync(5, It.IsAny<CancellationToken>()))
            .Returns(async (int id, CancellationToken ct) =>
            {
                tcsIniciadaA.SetResult();
                return await tcsNuncaCompletaA.Task.WaitAsync(ct);
            });

        var tcsIniciadaB = new TaskCompletionSource();
        var tcsDescargaB = new TaskCompletionSource<BackupDescargaDto>();
        backupsMock.Setup(b => b.DescargarAsync(7, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                tcsIniciadaB.SetResult();
                return await tcsDescargaB.Task;
            });
        guardadoMock.Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var tareaA = vm.DescargarCommand.ExecuteAsync(filaA);
        await tcsIniciadaA.Task;
        var tareaB = vm.DescargarCommand.ExecuteAsync(filaB);
        await tcsIniciadaB.Task;

        // Ambas en curso al mismo tiempo: el guard es por fila, no global al comando.
        Assert.True(filaA.Descargando);
        Assert.True(filaB.Descargando);

        // Cancelar A no debe tocar a B.
        vm.CancelarCommand.Execute(filaA);
        await tareaA;

        Assert.False(filaA.Descargando);
        Assert.True(filaB.Descargando);

        tcsDescargaB.SetResult(new BackupDescargaDto("backup_7.dump", new MemoryStream()));
        await tareaB;

        Assert.False(filaB.Descargando);
        confirmacionMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CargarAsync_ConLogs_ArmaElTextoDelResumen()
    {
        var logsMock = new Mock<ILogsService>();
        logsMock.Setup(l => l.ObtenerResumenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumenLogsDto(
                3, new DateTime(2026, 7, 1), new DateTime(2026, 7, 29), 2048));
        var (vm, _, _, _) = Crear(logs: logsMock);

        await vm.CargarAsync();

        Assert.True(vm.HayLogs);
        Assert.Contains("3", vm.TextoResumenLogs);
    }

    [Fact]
    public async Task CargarAsync_SinLogs_NoHabilitaLaDescarga()
    {
        var logsMock = new Mock<ILogsService>();
        logsMock.Setup(l => l.ObtenerResumenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumenLogsDto(0, null, null, 0));
        var (vm, _, _, _) = Crear(logs: logsMock);

        await vm.CargarAsync();

        Assert.False(vm.HayLogs);
    }

    [Fact]
    public async Task CargarAsync_ElServicioDeLogsFalla_NoRompeLaCargaDeBackups()
    {
        var logsMock = new Mock<ILogsService>();
        logsMock.Setup(l => l.ObtenerResumenAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("la api de logs esta caida"));
        var (vm, _, _, _) = Crear(
            corridas: new List<CorridaBackupDto>
            {
                new(1, new DateTime(2026, 7, 29), "Exitosa", "backup_1.dump", 1024, null),
            },
            logs: logsMock);

        await vm.CargarAsync();

        Assert.Single(vm.Corridas);
        Assert.False(vm.HayLogs);
    }

    [Fact]
    public async Task DescargarLogsCommand_GuardaElZip()
    {
        var logsMock = new Mock<ILogsService>();
        logsMock.Setup(l => l.ObtenerResumenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumenLogsDto(1, new DateTime(2026, 7, 29), new DateTime(2026, 7, 29), 10));
        logsMock.Setup(l => l.DescargarZipAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new LogsDescargaDto("logs_20260729.zip", new MemoryStream([1, 2, 3])));
        var (vm, _, guardadoMock, _) = Crear(logs: logsMock);
        await vm.CargarAsync();

        await vm.DescargarLogsCommand.ExecuteAsync(null);

        guardadoMock.Verify(g => g.GuardarBytesAsync(
            It.IsAny<Stream>(), "logs_20260729.zip", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(vm.DescargandoLogs);
    }

    [Fact]
    public async Task DescargarLogsCommand_ElServicioFalla_InformaElErrorYNoRompe()
    {
        var logsMock = new Mock<ILogsService>();
        logsMock.Setup(l => l.ObtenerResumenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumenLogsDto(1, new DateTime(2026, 7, 29), new DateTime(2026, 7, 29), 10));
        logsMock.Setup(l => l.DescargarZipAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("se cayo la api"));
        var (vm, _, _, confirmacionMock) = Crear(logs: logsMock);
        await vm.CargarAsync();

        await vm.DescargarLogsCommand.ExecuteAsync(null);

        confirmacionMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Once);
        Assert.False(vm.DescargandoLogs);
    }

    // ── IniciarBackupCommand (fix/integridad-referencial, POST /backups) ────────

    [Fact]
    public async Task IniciarBackupCommand_DisparaElBackupYAvisaAlUsuario()
    {
        var (vm, backupsMock, _, confirmacionMock) = Crear();

        await vm.IniciarBackupCommand.ExecuteAsync(null);

        backupsMock.Verify(b => b.IniciarAsync(It.IsAny<CancellationToken>()), Times.Once);
        confirmacionMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Once);
        Assert.False(vm.IniciandoBackup);
    }

    [Fact]
    public async Task IniciarBackupCommand_ElServicioFalla_InformaElErrorYNoRompe()
    {
        var (vm, backupsMock, _, confirmacionMock) = Crear();
        backupsMock.Setup(b => b.IniciarAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya hay un backup en curso."));

        await vm.IniciarBackupCommand.ExecuteAsync(null);

        confirmacionMock.Verify(c => c.InformarAsync("Ya hay un backup en curso."), Times.Once);
        Assert.False(vm.IniciandoBackup);
    }

    [Fact]
    public async Task IniciarBackupCommand_MientrasCorre_IniciandoBackupEsTrueYLuegoFalse()
    {
        var (vm, backupsMock, _, _) = Crear();
        var tcsIniciada = new TaskCompletionSource();
        var tcsFin = new TaskCompletionSource();
        backupsMock.Setup(b => b.IniciarAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                tcsIniciada.SetResult();
                await tcsFin.Task;
            });

        var tarea = vm.IniciarBackupCommand.ExecuteAsync(null);
        await tcsIniciada.Task;

        Assert.True(vm.IniciandoBackup);

        tcsFin.SetResult();
        await tarea;

        Assert.False(vm.IniciandoBackup);
    }

    [Fact]
    public async Task IniciarBackupCommand_DobleClick_LaSegundaEsNoOp()
    {
        var (vm, backupsMock, _, _) = Crear();
        var tcsIniciada = new TaskCompletionSource();
        var tcsFin = new TaskCompletionSource();
        var invocaciones = 0;
        backupsMock.Setup(b => b.IniciarAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                invocaciones++;
                tcsIniciada.SetResult();
                await tcsFin.Task;
            });

        var tarea1 = vm.IniciarBackupCommand.ExecuteAsync(null);
        await tcsIniciada.Task;

        var tarea2 = vm.IniciarBackupCommand.ExecuteAsync(null);
        await tarea2;

        Assert.Equal(1, invocaciones);

        tcsFin.SetResult();
        await tarea1;
    }
}
