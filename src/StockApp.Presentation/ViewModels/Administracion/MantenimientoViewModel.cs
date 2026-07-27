using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Backups;
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

    public ObservableCollection<FilaCorridaBackupVm> Corridas { get; } = new();

    [ObservableProperty]
    private bool _cargando;

    public MantenimientoViewModel(IBackupsService backups, IServicioGuardadoArchivo guardado, IConfirmacionService confirmacion)
    {
        _backups = backups;
        _guardado = guardado;
        _confirmacion = confirmacion;
    }

    public async Task CargarAsync()
    {
        Cargando = true;
        try
        {
            var lista = await _backups.ListarAsync();
            Corridas.Clear();
            foreach (var c in lista)
                Corridas.Add(new FilaCorridaBackupVm(c));
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
        finally
        {
            Cargando = false;
        }
    }

    [RelayCommand]
    private async Task DescargarAsync(FilaCorridaBackupVm fila)
    {
        fila.Cts = new CancellationTokenSource();
        fila.Descargando = true;
        try
        {
            await using var descarga = await _backups.DescargarAsync(fila.Id, fila.Cts.Token);
            await _guardado.GuardarBytesAsync(descarga.Contenido, descarga.NombreArchivo, fila.Cts.Token);
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
            fila.Cts?.Dispose();
            fila.Cts = null;
        }
    }

    [RelayCommand]
    private void Cancelar(FilaCorridaBackupVm fila) => fila.Cts?.Cancel();
}
