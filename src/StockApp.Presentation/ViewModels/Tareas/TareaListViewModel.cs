using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.ApiClient;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Tareas;

/// <summary>
/// Fila de solo lectura de la lista de tareas: aplana la entidad y agrega la visibilidad de
/// acciones según estado de la fila y rol del usuario logueado (spec: "un Operador no ve las
/// acciones de Admin"). DiasParaVencer alimenta SignoNegativoBrushConverter para el resaltado
/// de vencidas: negativo cuando la fecha límite pasó y el estado sigue abierto, 0 en cualquier
/// otro caso (sin fecha límite, o estado terminal).
/// </summary>
public sealed class TareaFila
{
    public Tarea Tarea { get; }
    private readonly RolUsuario _rol;

    public TareaFila(Tarea tarea, RolUsuario rol)
    {
        Tarea = tarea;
        _rol = rol;
    }

    public int Id => Tarea.Id;
    public string Titulo => Tarea.Titulo;
    public string PrioridadTexto => Tarea.Prioridad.ToString();
    public DateTime? FechaLimite => Tarea.FechaLimite;
    public string? TomadaPorNombre => Tarea.TomadaPor?.NombreUsuario;

    public decimal DiasParaVencer =>
        Tarea.FechaLimite is null || Tarea.Estado is EstadoTarea.Terminada or EstadoTarea.Cancelada
            ? 0m
            : (decimal)(Tarea.FechaLimite.Value.Date - DateTime.UtcNow.Date).TotalDays;

    public bool PuedeTomar    => Tarea.Estado == EstadoTarea.Pendiente;
    public bool PuedeSoltar   => Tarea.Estado == EstadoTarea.EnCurso;
    public bool PuedeTerminar => Tarea.Estado == EstadoTarea.EnCurso;

    public bool PuedeCancelar =>
        _rol == RolUsuario.Admin && Tarea.Estado is EstadoTarea.Pendiente or EstadoTarea.EnCurso;
}

/// <summary>
/// Pantalla "Tareas": lista agrupada por estado, canceladas detrás de un filtro (spec).
/// La vista dispara CargarAsync() vía DataContextChanged (convención del proyecto).
/// </summary>
public partial class TareaListViewModel : ViewModelBase
{
    private readonly ITareaService        _service;
    private readonly ICurrentSession      _session;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty] private bool _mostrarCanceladas;

    public ObservableCollection<TareaFila> Pendientes { get; } = new();
    public ObservableCollection<TareaFila> EnCurso { get; } = new();
    public ObservableCollection<TareaFila> Terminadas { get; } = new();
    public ObservableCollection<TareaFila> Canceladas { get; } = new();

    public TareaListViewModel(
        ITareaService service, ICurrentSession session,
        INavigationService navigation, IConfirmacionService confirmacion)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;
    }

    public async Task CargarAsync()
    {
        try
        {
            var tareas = await _service.ListarAsync();
            var rol = _session.RolActual ?? RolUsuario.Operador;

            Pendientes.Clear();
            EnCurso.Clear();
            Terminadas.Clear();
            Canceladas.Clear();

            foreach (var tarea in tareas)
            {
                var fila = new TareaFila(tarea, rol);
                switch (tarea.Estado)
                {
                    case EstadoTarea.Pendiente: Pendientes.Add(fila); break;
                    case EstadoTarea.EnCurso:   EnCurso.Add(fila); break;
                    case EstadoTarea.Terminada: Terminadas.Add(fila); break;
                    case EstadoTarea.Cancelada: Canceladas.Add(fila); break;
                }
            }
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    [RelayCommand]
    private void Nueva() => _navigation.Navegar<TareaFormViewModel>(vm => vm.CargarParaCrear());

    [RelayCommand]
    private void VerDetalle(TareaFila fila)
        => _navigation.Navegar<TareaFormViewModel>(vm => vm.CargarParaVer(fila.Tarea));

    [RelayCommand]
    private async Task TomarAsync(TareaFila fila)
    {
        try { await _service.TomarAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task SoltarAsync(TareaFila fila)
    {
        try { await _service.SoltarAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task TerminarAsync(TareaFila fila)
    {
        try { await _service.TerminarAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task CancelarAsync(TareaFila fila)
    {
        var confirmar = await _confirmacion.PreguntarAsync($"¿Confirma cancelar la tarea \"{fila.Titulo}\"?");
        if (!confirmar) return;

        try { await _service.CancelarAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    /// <summary>
    /// Único punto de traducción excepción → mensaje para todos los comandos de esta pantalla
    /// (fix real de IngresoPorFacturaViewModel, propagado acá: un AsyncRelayCommand no capturado
    /// no llega al handler global de Avalonia y termina en crash.log sin avisar al operario).
    /// Cualquier excepción no prevista cae en el catch-all como red de último recurso.
    /// </summary>
    private async Task ManejarErrorAsync(Exception ex)
    {
        var mensaje = ex switch
        {
            ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException
                or ServidorNoDisponibleException => ex.Message,
            UnauthorizedAccessException =>
                "La sesión expiró o no tiene permiso para realizar esta acción. Vuelva a iniciar sesión e intente de nuevo.",
            _ => "Ocurrió un error inesperado. Si el problema persiste, contactá a soporte.",
        };
        await _confirmacion.InformarAsync(mensaje);
    }
}
