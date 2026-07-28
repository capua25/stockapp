using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Backups;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Converters;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Reportes;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Pantalla de bienvenida mostrada en la región central del shell tras el login.
/// Resuelve el bug de "región central vacía tras login": es el primer contenido
/// navegado dentro de ShellMainViewModel una vez que este queda establecido como
/// CurrentViewModel del shell.
/// </summary>
public partial class InicioViewModel : ViewModelBase
{
    private readonly ICurrentSession        _session;
    private readonly INavigationService     _navigation;
    private readonly IFinanzasVistasService _finanzasVistas;
    private readonly IBackupsService        _backups;

    public string NombreUsuario =>
        _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Usuario";

    public string Saludo => $"¡Bienvenido, {NombreUsuario}!";

    public bool EsAdmin => _session.RolActual == RolUsuario.Admin;

    public string RolTexto => EsAdmin ? "Administrador" : "Operador";

    [ObservableProperty] private bool _mostrarAvisoVencimientos;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoVencidas))]
    private int _cantidadVencidas;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoAVencer7Dias))]
    private int _cantidadAVencer7Dias;

    public string TextoVencidas =>
        CantidadVencidas == 1 ? "1 factura vencida" : $"{CantidadVencidas} facturas vencidas";

    public string TextoAVencer7Dias =>
        CantidadAVencer7Dias == 1
            ? "1 factura por vencer esta semana"
            : $"{CantidadAVencer7Dias} facturas por vencer esta semana";

    // Tercer estado (review final E1): un fallo consultando /backups/salud (API caída, 403,
    // 404 por una versión vieja del servidor sin la ruta, cambio de forma del JSON) NO es lo
    // mismo que "backup al día" ni que "backup vencido" — antes el catch de más abajo ocultaba
    // el aviso, maquillando un fallo de consulta como salud OK, la inversión exacta del
    // principio de esta entrega. MostrarAvisoBackup sigue siendo "hay que avisar algo" (true
    // para Problema Y Desconocido); AvisoBackupEsDesconocido discrimina cuál de los dos, para
    // que la vista pueda mostrar textos y colores distintos sin afirmar ninguno de los otros
    // dos estados.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarAvisoBackupProblema))]
    [NotifyPropertyChangedFor(nameof(MostrarAvisoBackupDesconocido))]
    private bool _mostrarAvisoBackup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarAvisoBackupProblema))]
    [NotifyPropertyChangedFor(nameof(MostrarAvisoBackupDesconocido))]
    private bool _avisoBackupEsDesconocido;

    [ObservableProperty] private string? _textoAvisoBackup;

    public bool MostrarAvisoBackupProblema => MostrarAvisoBackup && !AvisoBackupEsDesconocido;

    public bool MostrarAvisoBackupDesconocido => MostrarAvisoBackup && AvisoBackupEsDesconocido;

    public InicioViewModel(
        ICurrentSession session, INavigationService navigation,
        IFinanzasVistasService finanzasVistas, IBackupsService backups)
    {
        _session        = session;
        _navigation     = navigation;
        _finanzasVistas = finanzasVistas;
        _backups        = backups;
    }

    /// <summary>
    /// Carga el aviso de vencimientos (spec §7.5: "al abrir la app, aviso en Inicio si hay
    /// facturas vencidas o por vencer en la semana"). Sin VerFinanzas o si la API falla, el
    /// aviso simplemente no se muestra — Inicio nunca debe romper (catch silencioso).
    /// </summary>
    public async Task CargarAsync()
    {
        try
        {
            var calendario = await _finanzasVistas.ObtenerCalendarioPagosAsync();
            CantidadVencidas = calendario.Vencidas.Count;
            CantidadAVencer7Dias = calendario.AVencer7Dias.Count;
            MostrarAvisoVencimientos = CantidadVencidas > 0 || CantidadAVencer7Dias > 0;
        }
        catch (Exception)
        {
            MostrarAvisoVencimientos = false;
        }

        if (!EsAdmin)
        {
            MostrarAvisoBackup = false;
            return;
        }

        try
        {
            var salud = await _backups.ObtenerSaludAsync();
            MostrarAvisoBackup = salud.Vencido;
            AvisoBackupEsDesconocido = false;
            // UmbralHoras viaja en el DTO (SaludBackupDto, Task 6) — NUNCA hardcodear el número
            // acá: si el umbral cambia en ServicioConsultaBackups, este texto tiene que reflejarlo
            // solo, sin quedar mintiendo en silencio (pre-flight scan, corregido).
            if (salud.UltimoExitoEn is DateTime ultimo)
            {
                // Hora LOCAL, no UTC cruda: mismo patrón que MantenimientoView.axaml
                // (FechaUtcALocalConverter) — un backup mostraba distinta hora según la pantalla.
                var local = (DateTime)FechaUtcALocalConverter.Instance.Convert(
                    ultimo, typeof(DateTime), null, CultureInfo.InvariantCulture)!;
                TextoAvisoBackup =
                    $"El último backup exitoso fue el {local:dd/MM/yyyy HH:mm} (hace más de {salud.UmbralHoras} horas).";
            }
            else
            {
                TextoAvisoBackup = "Todavía no se registró ningún backup exitoso.";
            }
        }
        catch (Exception)
        {
            // Tercer estado: NO se pudo verificar (ver comentario de AvisoBackupEsDesconocido
            // más arriba) — se avisa igual que un problema real, pero sin afirmar que el backup
            // está vencido ni que está al día.
            MostrarAvisoBackup = true;
            AvisoBackupEsDesconocido = true;
            TextoAvisoBackup = "No se pudo verificar el estado del backup.";
        }
    }

    // ── accesos rápidos: comunes (Admin + Operador) ───────────────────────────

    [RelayCommand]
    private void IrAProductos() => _navigation.Navegar<ProductoListViewModel>();

    [RelayCommand]
    private void IrARegistrarEntrada() => _navigation.Navegar<EntradaRegistroViewModel>();

    [RelayCommand]
    private void IrARegistrarSalida() => _navigation.Navegar<SalidaRegistroViewModel>();

    [RelayCommand]
    private void IrAHistorialMovimientos() => _navigation.Navegar<MovimientoHistorialViewModel>();

    [RelayCommand]
    private void IrACalendarioPagos() => _navigation.Navegar<CalendarioPagosViewModel>();

    // ── accesos rápidos: solo Admin ────────────────────────────────────────────

    [RelayCommand]
    private void IrAValorizacion() => _navigation.Navegar<ValorizacionViewModel>();

    [RelayCommand]
    private void IrAAuditoria() => _navigation.Navegar<AuditoriaLogViewModel>();
}
