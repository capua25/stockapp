using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Backups;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

/// <summary>
/// KPIs de Inicio v2 (spec §5.3): valor, estado sin dato y gates; fecha y horas desde un reloj inyectado.
/// </summary>
public class InicioViewModelMetricasTests
{
    private sealed class RelojFijo(DateTimeOffset ahora, TimeZoneInfo? zona = null) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
        public override TimeZoneInfo LocalTimeZone => zona ?? TimeZoneInfo.Utc;
    }

    // Montevideo: UTC-3 fijo, creado a mano para no depender de la base de zonas del SO.
    private static readonly TimeZoneInfo Montevideo =
        TimeZoneInfo.CreateCustomTimeZone("Montevideo-test", TimeSpan.FromHours(-3), "Montevideo", "Montevideo");

    // 2030: lejos del reloj real, así un VM que lea DateTime.UtcNow no puede coincidir por casualidad.
    private static readonly DateTimeOffset Ahora = new(2030, 3, 15, 13, 0, 0, TimeSpan.Zero);

    private static readonly UsuarioSesion Admin = new(1, "admin", RolUsuario.Admin, "Administrador General");
    private static readonly UsuarioSesion Operador = new(2, "juan", RolUsuario.Operador, "Juan Pérez");

    private static CalendarioPagosDto Calendario(int vencidas, int aVencer)
    {
        FacturaCalendarioDto Factura(int id) => new(id, "Proveedor", $"A-{id}", 100m, new DateOnly(2030, 3, 18), "Pendiente");
        return new CalendarioPagosDto(
            Enumerable.Range(1, vencidas).Select(Factura).ToList(),
            Enumerable.Range(100, aVencer).Select(Factura).ToList(),
            new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>());
    }

    private static Tarea Tarea(int id, DateTime limiteUtc) => new()
    {
        Id = id, Titulo = $"Tarea {id}", Estado = EstadoTarea.Pendiente, Prioridad = PrioridadTarea.Media,
        CreadaPorUsuarioId = 1, FechaCreacion = limiteUtc.AddDays(-30), FechaLimite = limiteUtc,
    };

    private sealed record Servicios(
        Mock<ICurrentSession> Sesion, Mock<IFinanzasVistasService> Finanzas, Mock<ITareaService> Tareas,
        Mock<IBackupsService> Backups, Mock<IAuthService> Auth);

    private static (InicioViewModel Vm, Servicios S) Crear(UsuarioSesion usuario, params string[] permisos) =>
        CrearConReloj(new RelojFijo(Ahora), usuario, permisos);

    private static (InicioViewModel Vm, Servicios S) CrearConReloj(
        TimeProvider reloj, UsuarioSesion usuario, params string[] permisos)
    {
        var s = new Servicios(new(), new(), new(), new(), new());
        s.Sesion.Setup(x => x.UsuarioActual).Returns(usuario);
        s.Sesion.Setup(x => x.RolActual).Returns(usuario.Rol);
        s.Sesion.Setup(x => x.PermisosActuales).Returns(new HashSet<string>(permisos));
        s.Finanzas.Setup(x => x.ObtenerCalendarioPagosAsync(It.IsAny<DateTime?>())).ReturnsAsync(Calendario(0, 0));
        s.Tareas.Setup(x => x.ListarAsync()).ReturnsAsync(new List<Tarea>());
        s.Backups.Setup(x => x.ObtenerSaludAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaludBackupDto(Ahora.UtcDateTime.AddHours(-1), false, 26));
        s.Auth.Setup(x => x.ObtenerPermisosPropiosAsync()).ReturnsAsync((IReadOnlySet<string>)new HashSet<string>(permisos));
        var vm = new InicioViewModel(s.Sesion.Object, Mock.Of<INavigationService>(), s.Finanzas.Object, s.Backups.Object,
            s.Tareas.Object, s.Auth.Object, reloj);
        return (vm, s);
    }

    [Fact]
    public void Subtitulo_FechaLargaDelRelojYRol()
    {
        var (vm, _) = Crear(Operador);

        Assert.Equal("viernes 15 de marzo de 2030 · Sesión iniciada como Operador", vm.Subtitulo);
    }

    [Fact]
    public void AntesDeCargar_LasTresMetricasEstanSinDato()
    {
        // Un "0" antes de consultar afirmaría que no hay nada vencido.
        var (vm, _) = Crear(Admin);

        Assert.True(vm.FacturasAVencerSinDato);
        Assert.True(vm.TareasVencidasSinDato);
        Assert.True(vm.UltimoBackupSinDato);
    }

    [Fact]
    public async Task CalendarioConDatos_ValorDeFacturasYAlertaDeVencidas()
    {
        var (vm, s) = Crear(Operador, Permisos.VerFinanzas);
        s.Finanzas.Setup(x => x.ObtenerCalendarioPagosAsync(It.IsAny<DateTime?>())).ReturnsAsync(Calendario(1, 3));

        await vm.CargarAsync();

        Assert.False(vm.FacturasAVencerSinDato);
        Assert.Equal("3", vm.ValorFacturasAVencer);
        Assert.True(vm.HayFacturasVencidas);
        Assert.Equal("1 factura vencida", vm.TextoVencidas);
    }

    [Fact]
    public async Task CalendarioFalla_FacturasSinDato()
    {
        var (vm, s) = Crear(Operador, Permisos.VerFinanzas);
        s.Finanzas.Setup(x => x.ObtenerCalendarioPagosAsync(It.IsAny<DateTime?>())).ThrowsAsync(new HttpRequestException("API caída"));

        await vm.CargarAsync();

        Assert.True(vm.FacturasAVencerSinDato);
        Assert.False(vm.HayFacturasVencidas);
    }

    [Fact]
    public async Task TareasFallan_TareasSinDato()
    {
        var (vm, s) = Crear(Operador, Permisos.GestionarTareas);
        s.Tareas.Setup(x => x.ListarAsync()).ThrowsAsync(new HttpRequestException("API caída"));

        await vm.CargarAsync();

        Assert.True(vm.TareasVencidasSinDato);
    }

    [Fact]
    public async Task ConRelojFijo_FechaDiasYBackupSalenDelReloj()
    {
        // Límite 14/03/2030 con el reloj en 15/03/2030: vencida hace 1 día. Con el reloj real (2026) esa
        // tarea estaría a años de vencer y no entraría al panel.
        var (vm, s) = Crear(Admin);
        s.Tareas.Setup(x => x.ListarAsync()).ReturnsAsync(new List<Tarea> { Tarea(1, new DateTime(2030, 3, 14, 0, 0, 0, DateTimeKind.Utc)) });
        s.Backups.Setup(x => x.ObtenerSaludAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaludBackupDto(new DateTime(2030, 3, 13, 14, 32, 0, DateTimeKind.Utc), true, 26));

        await vm.CargarAsync();

        Assert.False(vm.TareasVencidasSinDato);
        Assert.Equal("1", vm.ValorTareasVencidas);
        Assert.Equal("-1 día", Assert.Single(vm.TareasVencidas).TextoDiasParaVencer);
        Assert.False(vm.UltimoBackupSinDato);
        Assert.Equal("13/03/2030 14:32", vm.ValorUltimoBackup);
        Assert.Equal("El último backup exitoso fue el 13/03/2030 14:32 (hace más de 26 horas).", vm.TextoAvisoBackup);
    }

    [Fact]
    public async Task RecargaFallidaTrasCargaExitosa_NoDejaAlertaDeVencidasJuntoAlSinDato()
    {
        var (vm, s) = Crear(Operador, Permisos.VerFinanzas);
        s.Finanzas.Setup(x => x.ObtenerCalendarioPagosAsync(It.IsAny<DateTime?>())).ReturnsAsync(Calendario(2, 3));
        await vm.CargarAsync();
        Assert.True(vm.HayFacturasVencidas);

        s.Finanzas.Setup(x => x.ObtenerCalendarioPagosAsync(It.IsAny<DateTime?>())).ThrowsAsync(new HttpRequestException("API caída"));
        await vm.CargarAsync();

        Assert.True(vm.FacturasAVencerSinDato);
        Assert.False(vm.HayFacturasVencidas);
        Assert.Equal(0, vm.CantidadVencidas);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    public async Task ZonaNoUtc_BackupYSubtituloSeConvierteUnaVezYCambianDeDiaCercaDeMedianoche(DateTimeKind kind)
    {
        // 15/03/2030 02:00 UTC = 14/03/2030 23:00 en UTC-3; backup 15/03 01:30 UTC = 14/03 22:30 local.
        var reloj = new RelojFijo(new DateTimeOffset(2030, 3, 15, 2, 0, 0, TimeSpan.Zero), Montevideo);
        var (vm, s) = CrearConReloj(reloj, Admin);
        s.Backups.Setup(x => x.ObtenerSaludAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaludBackupDto(new DateTime(2030, 3, 15, 1, 30, 0, kind), true, 26));

        await vm.CargarAsync();

        Assert.Equal("jueves 14 de marzo de 2030 · Sesión iniciada como Administrador", vm.Subtitulo);
        Assert.Equal("14/03/2030 22:30", vm.ValorUltimoBackup);
        Assert.Equal("El último backup exitoso fue el 14/03/2030 22:30 (hace más de 26 horas).", vm.TextoAvisoBackup);
    }

    [Fact]
    public async Task SinNingunBackup_UltimoBackupSinDato()
    {
        var (vm, s) = Crear(Admin);
        s.Backups.Setup(x => x.ObtenerSaludAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SaludBackupDto(null, true, 26));

        await vm.CargarAsync();

        Assert.True(vm.UltimoBackupSinDato);
        Assert.Null(vm.ValorUltimoBackup);
    }

    [Fact]
    public async Task BackupFalla_UltimoBackupSinDato()
    {
        var (vm, s) = Crear(Admin);
        s.Backups.Setup(x => x.ObtenerSaludAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("API caída"));

        await vm.CargarAsync();

        Assert.True(vm.UltimoBackupSinDato);
        Assert.True(vm.MostrarAvisoBackupDesconocido);
    }

    [Theory]
    [InlineData(false, new string[0], false)]
    [InlineData(false, new[] { Permisos.GestionarProductos, Permisos.VerReportes }, false)]
    [InlineData(false, new[] { Permisos.VerFinanzas }, true)]
    [InlineData(false, new[] { Permisos.GestionarTareas }, true)]
    [InlineData(true, new string[0], true)]
    public void MostrarFilaMetricas_SiAlgunaMetricaCumpleSuGate(bool esAdmin, string[] permisos, bool esperado)
    {
        var (vm, _) = Crear(esAdmin ? Admin : Operador, permisos);

        Assert.Equal(esperado, vm.MostrarFilaMetricas);
    }

    [Fact]
    public async Task RefrescarPermisos_NotificaLosGatesDeLasMetricas()
    {
        // Sin estas notificaciones una métrica revocada en caliente sigue a la vista.
        var (vm, _) = Crear(Operador, Permisos.VerFinanzas, Permisos.GestionarTareas);
        var notificadas = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => notificadas.Add(e.PropertyName);

        await vm.CargarAsync();
        await vm._tareaRefrescoPermisos;

        Assert.Contains(nameof(InicioViewModel.PuedeVerCalendarioPagos), notificadas);
        Assert.Contains(nameof(InicioViewModel.PuedeVerTareas), notificadas);
        Assert.Contains(nameof(InicioViewModel.MostrarFilaMetricas), notificadas);
    }

    [Fact]
    public void Contenedor_ResuelveElVmSinRegistrarUnReloj()
    {
        // App.axaml.cs registra AddTransient<InicioViewModel>() y ningún TimeProvider: el parámetro opcional
        // tiene que caer en su valor por defecto (TimeProvider.System), no romper la resolución.
        var servicios = new ServiceCollection()
            .AddSingleton(Mock.Of<ICurrentSession>())
            .AddSingleton(Mock.Of<INavigationService>())
            .AddSingleton(Mock.Of<IFinanzasVistasService>())
            .AddSingleton(Mock.Of<IBackupsService>())
            .AddSingleton(Mock.Of<ITareaService>())
            .AddSingleton(Mock.Of<IAuthService>())
            .AddTransient<InicioViewModel>()
            .BuildServiceProvider();

        Assert.NotNull(servicios.GetRequiredService<InicioViewModel>());
    }
}
