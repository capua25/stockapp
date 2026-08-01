using System;
using System.Collections.Generic;
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
/// Doble uso (spec: "el panel de detalle muestra la descripción y el hilo de notas"): modo
/// alta (EsNuevaTarea = true) para crear una tarea nueva, y modo detalle (EsNuevaTarea =
/// false) para ver una tarea existente, su hilo de notas y —solo Admin— cambiarle la
/// prioridad. Título/descripción/fecha límite NO se editan después de creada: ninguna
/// acción del módulo lo permite (fuera de alcance del spec: "reasignación explícita").
/// Cargar* son síncronos (sin combos que precargar), por eso TareaFormView.axaml.cs no
/// necesita wiring de DataContextChanged.
/// </summary>
public partial class TareaFormViewModel : ViewModelBase
{
    private readonly ITareaService        _service;
    private readonly ICurrentSession      _session;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    private int _idTarea;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _titulo = string.Empty;

    [ObservableProperty] private string? _descripcion;
    [ObservableProperty] private DateTime? _fechaLimiteSeleccionada;
    [ObservableProperty] private string _estadoTexto = string.Empty;
    [ObservableProperty] private string? _tomadaPorNombre;
    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuestraCambioPrioridad))]
    private bool _esNuevaTarea = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AgregarNotaCommand))]
    private string _nuevaNotaTexto = string.Empty;

    [ObservableProperty] private PrioridadTarea _prioridadSeleccionada;

    public ObservableCollection<NotaTarea> Notas { get; } = new();
    public IReadOnlyList<PrioridadTarea> PrioridadesDisponibles { get; } =
        new[] { PrioridadTarea.Baja, PrioridadTarea.Media, PrioridadTarea.Alta };

    public bool EsAdmin => _session.RolActual == RolUsuario.Admin;
    public bool MuestraCambioPrioridad => EsAdmin && !EsNuevaTarea;

    public TareaFormViewModel(
        ITareaService service, ICurrentSession session,
        INavigationService navigation, IConfirmacionService confirmacion)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;
    }

    public void CargarParaCrear()
    {
        _idTarea = 0;
        EsNuevaTarea = true;
        Titulo = string.Empty;
        Descripcion = null;
        FechaLimiteSeleccionada = null;
        EstadoTexto = string.Empty;
        TomadaPorNombre = null;
        MensajeError = null;
        Notas.Clear();
    }

    public void CargarParaVer(Tarea tarea)
    {
        _idTarea = tarea.Id;
        EsNuevaTarea = false;
        Titulo = tarea.Titulo;
        Descripcion = tarea.Descripcion;
        FechaLimiteSeleccionada = tarea.FechaLimite;
        EstadoTexto = tarea.Estado.ToString();
        TomadaPorNombre = tarea.TomadaPor?.NombreUsuario;
        PrioridadSeleccionada = tarea.Prioridad;
        MensajeError = null;

        Notas.Clear();
        foreach (var nota in tarea.Notas)
            Notas.Add(nota);
    }

    private bool PuedeGuardar() => !string.IsNullOrWhiteSpace(Titulo);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            await _service.CrearAsync(new Tarea
            {
                Titulo = Titulo,
                Descripcion = string.IsNullOrWhiteSpace(Descripcion) ? null : Descripcion,
                FechaLimite = FechaLimiteSeleccionada,
            });
            _navigation.Navegar<TareaListViewModel>();
        }
        catch (Exception ex)
        {
            MensajeError = ResolverMensajeError(ex);
        }
    }

    private bool PuedeAgregarNota() => !string.IsNullOrWhiteSpace(NuevaNotaTexto);

    [RelayCommand(CanExecute = nameof(PuedeAgregarNota))]
    private async Task AgregarNotaAsync()
    {
        MensajeError = null;
        var texto = NuevaNotaTexto;
        try
        {
            await _service.AgregarNotaAsync(_idTarea, texto);
            Notas.Add(new NotaTarea { TareaId = _idTarea, Texto = texto, Fecha = DateTime.UtcNow, EsAutomatica = false });
            NuevaNotaTexto = string.Empty;
        }
        catch (Exception ex)
        {
            MensajeError = ResolverMensajeError(ex);
        }
    }

    [RelayCommand]
    private async Task CambiarPrioridadAsync()
    {
        MensajeError = null;
        try
        {
            await _service.CambiarPrioridadAsync(_idTarea, PrioridadSeleccionada);
            await _confirmacion.InformarAsync($"Prioridad actualizada a {PrioridadSeleccionada}.");
        }
        catch (Exception ex)
        {
            MensajeError = ResolverMensajeError(ex);
        }
    }

    [RelayCommand]
    private void Volver() => _navigation.Navegar<TareaListViewModel>();

    /// <summary>
    /// Único punto de traducción excepción → mensaje para los comandos de esta pantalla (fix
    /// real de IngresoPorFacturaViewModel, propagado acá: un AsyncRelayCommand no capturado no
    /// llega al handler global de Avalonia y termina en crash.log sin avisar al operario).
    /// Cualquier excepción no prevista cae en el catch-all como red de último recurso.
    /// </summary>
    private static string ResolverMensajeError(Exception ex) => ex switch
    {
        ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException
            or ServidorNoDisponibleException => ex.Message,
        UnauthorizedAccessException =>
            "La sesión expiró o no tiene permiso para realizar esta acción. Vuelva a iniciar sesión e intente de nuevo.",
        _ => "Ocurrió un error inesperado. Si el problema persiste, contactá a soporte.",
    };
}
