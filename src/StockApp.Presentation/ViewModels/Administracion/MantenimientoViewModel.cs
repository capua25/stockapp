using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Backups;
using StockApp.Application.Logs;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Administracion;

/// <summary>
/// Zona "Backups" de la pantalla de Mantenimiento (spec Backups §7, Entrega 1 — la única zona
/// de esta entrega; la Entrega 2 agrega "Diagnóstico" al mismo VM/View, no crea uno nuevo).
/// Primera pantalla de la sección "Administración" del sidebar, Admin-only.
/// </summary>
public partial class MantenimientoViewModel : ViewModelBase
{
    private readonly IBackupsService _backups;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IConfirmacionService _confirmacion;
    private readonly ILogsService _logs;

    public ObservableCollection<FilaCorridaBackupVm> Corridas { get; } = new();

    [ObservableProperty]
    private string _textoResumenLogs = "Sin datos de logs todavía.";

    [ObservableProperty]
    private bool _hayLogs;

    [ObservableProperty]
    private bool _descargandoLogs;

    // MostrarListaVacia solo se recalcula cuando cambia Cargando, no Corridas.CollectionChanged
    // directamente: CargarAsync() mutila Corridas (Clear/Add) de forma síncrona, ANTES de que el
    // finally ponga Cargando en false, así que cuando el binding se re-evalúa el conteo ya está
    // definitivo. Evita colgar un handler de CollectionChanged para un caso que ya está cubierto.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarListaVacia))]
    private bool _cargando;

    /// <summary>
    /// Estado vacío explícito (review final E1): sin esto, una instalación nueva sin backups
    /// deja la pantalla en blanco bajo el subtítulo, indistinguible de "falló algo y no me
    /// enteré". Solo se muestra una vez terminada la carga, para no destellar antes de que
    /// Corridas se pueble. Excluye explícitamente el caso de error (ver <see cref="ErrorAlCargar"/>):
    /// si ListarAsync() falla, Corridas también queda en 0, pero eso NO es "no hay backups".
    /// </summary>
    public bool MostrarListaVacia => !Cargando && !ErrorAlCargar && Corridas.Count == 0;

    /// <summary>
    /// Fix (MINOR, re-review final E1): si ListarAsync() falla en la primera carga, Corridas
    /// queda vacío y sin este flag se mostraba "Todavía no hay backups registrados." — el
    /// mensaje de "todo bien" para un caso que es un error. InformarAsync ya avisa con un
    /// diálogo, pero el texto de la pantalla quedaba mintiendo sobre la causa después de
    /// cerrarlo. Misma clase de bug que el banner de Inicio (Inc 7), acá en miniatura.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarListaVacia))]
    private bool _errorAlCargar;

    public MantenimientoViewModel(
        IBackupsService backups,
        IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion,
        ILogsService logs)
    {
        _backups = backups;
        _guardado = guardado;
        _confirmacion = confirmacion;
        _logs = logs;
    }

    public async Task CargarAsync()
    {
        Cargando = true;
        ErrorAlCargar = false;
        try
        {
            var lista = await _backups.ListarAsync();
            Corridas.Clear();
            foreach (var c in lista)
                Corridas.Add(new FilaCorridaBackupVm(c));
        }
        catch (Exception ex)
        {
            ErrorAlCargar = true;
            await _confirmacion.InformarAsync(ex.Message);
        }
        finally
        {
            Cargando = false;
        }

        await CargarResumenLogsAsync();
    }

    /// <summary>
    /// El resumen de logs se carga aparte y se traga sus propios errores: que el
    /// diagnostico no esté disponible no puede dejar la lista de backups en blanco.
    /// </summary>
    private async Task CargarResumenLogsAsync()
    {
        try
        {
            var resumen = await _logs.ObtenerResumenAsync();
            HayLogs = resumen.CantidadArchivos > 0;
            TextoResumenLogs = HayLogs
                ? $"{resumen.CantidadArchivos} archivo(s), {FormatearTamanio(resumen.TamanioTotalBytes)}, "
                  + $"del {resumen.DesdeFecha:dd/MM/yyyy} al {resumen.HastaFecha:dd/MM/yyyy}."
                : "No hay archivos de log todavía.";
        }
        catch (Exception)
        {
            HayLogs = false;
            TextoResumenLogs = "No se pudo consultar el estado de los logs.";
        }
    }

    private static string FormatearTamanio(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };

    [RelayCommand]
    private async Task DescargarLogsAsync()
    {
        if (DescargandoLogs) return;

        DescargandoLogs = true;
        try
        {
            await using var descarga = await _logs.DescargarZipAsync();
            await _guardado.GuardarBytesAsync(descarga.Contenido, descarga.NombreArchivo);
        }
        catch (OperationCanceledException) { /* cancelación deliberada, no se informa */ }
        catch (Exception ex) { await _confirmacion.InformarAsync(ex.Message); }
        finally { DescargandoLogs = false; }
    }

    [RelayCommand]
    private async Task DescargarAsync(FilaCorridaBackupVm fila)
    {
        // Guard explícito por fila (no [RelayCommand(AllowConcurrentExecutions = false)]): esa
        // opción es global al comando, no por parámetro — bloquearía descargar dos filas DISTINTAS
        // en paralelo, que es justamente el diseño (ver comentario de Cts en FilaCorridaBackupVm).
        // La comprobación y el `Descargando = true` de más abajo corren sin ningún await entre
        // medio, así que un doble click reentrante en la MISMA fila siempre ve el flag ya en true.
        if (fila.Descargando)
            return;

        // CTS local: el ciclo de vida (creación, cancelación vía Cts.Token, disposición) se maneja
        // sobre esta variable, nunca releyendo fila.Cts — así esta invocación nunca dispone ni
        // pierde de vista el CTS de otra invocación.
        var cts = new CancellationTokenSource();
        fila.Cts = cts;
        fila.Descargando = true;
        try
        {
            await using var descarga = await _backups.DescargarAsync(fila.Id, cts.Token);
            await _guardado.GuardarBytesAsync(descarga.Contenido, descarga.NombreArchivo, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancelación deliberada del usuario (CancelarCommand, más abajo) — NO es un error,
            // no se informa (ver decisión de diseño del Task).
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
        finally
        {
            fila.Descargando = false;
            if (ReferenceEquals(fila.Cts, cts))
                fila.Cts = null;
            cts.Dispose();
        }
    }

    [RelayCommand]
    private void Cancelar(FilaCorridaBackupVm fila) => fila.Cts?.Cancel();
}
