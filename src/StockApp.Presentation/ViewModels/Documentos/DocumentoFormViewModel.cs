using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.ApiClient;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Documentos;

/// <summary>
/// Doble uso (molde: TareaFormViewModel): modo alta (EsNuevoDocumento = true) para registrar
/// un documento nuevo, y modo detalle/edición (EsNuevoDocumento = false) para ver un
/// documento existente, su hilo de eventos, editarlo mientras esté activo (D1) y ejecutar
/// las transiciones de estado. El panel de adjuntos (Task 21) se inyecta ya construido
/// (Transient, mismo ciclo de vida que este VM) y se inicializa recién en CargarParaVerAsync,
/// porque agregar adjuntos exige que el documento ya exista (D11a).
/// </summary>
public partial class DocumentoFormViewModel : ViewModelBase
{
    /// <summary>
    /// Mensaje para UnauthorizedAccessException. No se usa hoy (el catch de 403 es silencioso,
    /// spec "Manejo de errores") pero se deja documentado el motivo por si el criterio cambia.
    /// </summary>
    public const string MensajeSinPermiso =
        "La sesión expiró o no tiene permiso para realizar esta acción. Vuelva a iniciar sesión e intente de nuevo.";

    private readonly IDocumentoAdministrativoService _service;
    private readonly ICurrentSession                 _session;
    private readonly INavigationService               _navigation;
    private readonly IConfirmacionService              _confirmacion;

    private DocumentoAdministrativo? _documento;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    [NotifyCanExecuteChangedFor(nameof(GuardarEdicionCommand))]
    private string _numero = string.Empty;

    [ObservableProperty] private int _anioSeleccionado = DateTime.UtcNow.Year;
    [ObservableProperty] private TipoDocumento _tipoSeleccionado = TipoDocumento.Expediente;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    [NotifyCanExecuteChangedFor(nameof(GuardarEdicionCommand))]
    private DateTime? _fechaEmisionSeleccionada;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    [NotifyCanExecuteChangedFor(nameof(GuardarEdicionCommand))]
    private string? _descripcion;

    [ObservableProperty] private string _estadoTexto = string.Empty;
    [ObservableProperty] private string? _registradoPorNombre;
    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeEditar))]
    [NotifyPropertyChangedFor(nameof(PuedeEditarCampos))]
    [NotifyPropertyChangedFor(nameof(PuedeIniciar))]
    [NotifyPropertyChangedFor(nameof(PuedeVolverAPendiente))]
    [NotifyPropertyChangedFor(nameof(PuedeFinalizar))]
    [NotifyPropertyChangedFor(nameof(PuedeAnular))]
    [NotifyPropertyChangedFor(nameof(PuedeReabrir))]
    private bool _esNuevoDocumento = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AgregarNotaCommand))]
    private string _nuevaNotaTexto = string.Empty;

    public ObservableCollection<EventoDocumento> Eventos { get; } = new();

    public IReadOnlyList<TipoDocumento> TiposDisponibles { get; } =
        new[] { TipoDocumento.Expediente, TipoDocumento.Oficio, TipoDocumento.Suministro };

    public AdjuntosDocumentoPanelViewModel AdjuntosPanel { get; }

    public bool EsAdmin => _session.RolActual == RolUsuario.Admin;

    public bool PuedeEditar          => !EsNuevoDocumento && _documento is { EsActivo: true };

    /// <summary>
    /// Controla si los 5 campos del documento (Numero/AnioSeleccionado/TipoSeleccionado/
    /// FechaEmisionSeleccionada/Descripcion) están habilitados en el XAML: en alta siempre
    /// (EsNuevoDocumento), en detalle solo si PuedeEditar (D1: documento activo). Evita
    /// duplicar el bloque de campos en el XAML para alta vs. detalle -- un único bloque con
    /// IsEnabled="{Binding PuedeEditarCampos}" cubre ambos modos.
    /// </summary>
    public bool PuedeEditarCampos => EsNuevoDocumento || PuedeEditar;

    // Mismo fix que DocumentoFila.PuedeIniciar (C6): Finalizado/Anulado -> EnProceso también es
    // válido en el dominio (es la reapertura), así que PuedeTransicionarA(EnProceso) solo no
    // alcanza -- hace falta exigir además que el documento esté Pendiente.
    public bool PuedeIniciar         => !EsNuevoDocumento && (_documento?.Estado == EstadoDocumento.Pendiente) && (_documento?.PuedeTransicionarA(EstadoDocumento.EnProceso) ?? false);
    public bool PuedeVolverAPendiente => !EsNuevoDocumento && (_documento?.PuedeTransicionarA(EstadoDocumento.Pendiente) ?? false);
    public bool PuedeFinalizar       => !EsNuevoDocumento && (_documento?.PuedeTransicionarA(EstadoDocumento.Finalizado) ?? false);
    public bool PuedeAnular          => EsAdmin && !EsNuevoDocumento && (_documento?.PuedeTransicionarA(EstadoDocumento.Anulado) ?? false);
    public bool PuedeReabrir         => EsAdmin && !EsNuevoDocumento && (_documento?.EsCerrado ?? false);

    public DocumentoFormViewModel(
        IDocumentoAdministrativoService service, ICurrentSession session,
        INavigationService navigation, IConfirmacionService confirmacion,
        AdjuntosDocumentoPanelViewModel adjuntosPanel)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;
        AdjuntosPanel = adjuntosPanel;
    }

    public void CargarParaCrear()
    {
        _documento = null;
        EsNuevoDocumento = true;
        Numero = string.Empty;
        AnioSeleccionado = DateTime.UtcNow.Year;
        TipoSeleccionado = TipoDocumento.Expediente;
        FechaEmisionSeleccionada = DateTime.UtcNow.Date;
        Descripcion = null;
        EstadoTexto = string.Empty;
        RegistradoPorNombre = null;
        MensajeError = null;
        Eventos.Clear();
    }

    public async Task CargarParaVerAsync(DocumentoAdministrativo documento)
    {
        _documento = documento;
        EsNuevoDocumento = false;
        CargarCamposDesdeDocumento(documento);
        MensajeError = null;

        await AdjuntosPanel.InicializarAsync(documento.Id, documento.EsActivo);
    }

    private void CargarCamposDesdeDocumento(DocumentoAdministrativo documento)
    {
        Numero = documento.Numero;
        AnioSeleccionado = documento.Anio;
        TipoSeleccionado = documento.Tipo;
        FechaEmisionSeleccionada = documento.FechaEmision;
        Descripcion = documento.Descripcion;
        EstadoTexto = documento.Estado.ToString();
        RegistradoPorNombre = documento.RegistradoPor?.NombreUsuario;

        Eventos.Clear();
        foreach (var evento in documento.Eventos.OrderBy(e => e.Fecha))
            Eventos.Add(evento);
    }

    /// <summary>
    /// Refresca el documento desde el servidor y vuelve a poblar los campos + los booleanos
    /// Puede* -- se usa después de CUALQUIER acción que mute el documento (iniciar, volver a
    /// pendiente, finalizar, anular, reabrir, editar, agregar nota) para que el hilo de
    /// eventos y el estado mostrado sean siempre los reales, no una copia local optimista.
    /// </summary>
    private async Task RecargarAsync()
    {
        if (_documento is null) return;

        var documento = await _service.ObtenerPorIdAsync(_documento.Id);
        if (documento is null)
        {
            // F5: ObtenerPorIdAsync es nullable -- si el documento ya no existe (borrado o
            // inaccesible entre acciones), no hay nada que recargar; se avisa con el mismo
            // canal que ManejarErrorAsync (MensajeError) en vez de reventar con NRE.
            MensajeError = "El documento ya no existe. Volvé a la lista e intentá de nuevo.";
            return;
        }

        _documento = documento;
        CargarCamposDesdeDocumento(_documento);
        OnPropertyChanged(nameof(PuedeEditar));
        OnPropertyChanged(nameof(PuedeEditarCampos));
        OnPropertyChanged(nameof(PuedeIniciar));
        OnPropertyChanged(nameof(PuedeVolverAPendiente));
        OnPropertyChanged(nameof(PuedeFinalizar));
        OnPropertyChanged(nameof(PuedeAnular));
        OnPropertyChanged(nameof(PuedeReabrir));
    }

    private bool PuedeGuardar() => !string.IsNullOrWhiteSpace(Numero) && !string.IsNullOrWhiteSpace(Descripcion)
        && FechaEmisionSeleccionada.HasValue;

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            await _service.RegistrarAsync(new DocumentoAdministrativo
            {
                Numero = Numero,
                Anio = AnioSeleccionado,
                Tipo = TipoSeleccionado,
                FechaEmision = DateTime.SpecifyKind(FechaEmisionSeleccionada!.Value.Date, DateTimeKind.Utc),
                Descripcion = Descripcion!,
            });
            _navigation.Navegar<DocumentoListViewModel>();
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarEdicionAsync()
    {
        if (_documento is null) return;
        MensajeError = null;
        try
        {
            await _service.EditarAsync(_documento.Id, new DatosEdicionDocumento(
                Numero, AnioSeleccionado, TipoSeleccionado,
                DateTime.SpecifyKind(FechaEmisionSeleccionada!.Value.Date, DateTimeKind.Utc), Descripcion!));
            await RecargarAsync();
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    [RelayCommand]
    private async Task IniciarAsync()
    {
        if (_documento is null) return;
        try { await _service.IniciarProcesoAsync(_documento.Id); await RecargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task VolverAPendienteAsync()
    {
        if (_documento is null) return;
        try { await _service.VolverAPendienteAsync(_documento.Id); await RecargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task FinalizarAsync()
    {
        if (_documento is null) return;
        try { await _service.FinalizarAsync(_documento.Id); await RecargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task AnularAsync()
    {
        if (_documento is null) return;

        var motivo = await _confirmacion.PedirTextoAsync(
            "Anular documento", "Ingresá el motivo de la anulación:");
        if (string.IsNullOrWhiteSpace(motivo)) return;

        try { await _service.AnularAsync(_documento.Id, motivo); await RecargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task ReabrirAsync()
    {
        if (_documento is null) return;

        var motivo = await _confirmacion.PedirTextoAsync(
            "Reabrir documento", "Ingresá el motivo de la reapertura:");
        if (string.IsNullOrWhiteSpace(motivo)) return;

        try { await _service.ReabrirAsync(_documento.Id, motivo); await RecargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    private bool PuedeAgregarNota() => !string.IsNullOrWhiteSpace(NuevaNotaTexto);

    [RelayCommand(CanExecute = nameof(PuedeAgregarNota))]
    private async Task AgregarNotaAsync()
    {
        if (_documento is null) return;
        var texto = NuevaNotaTexto;
        try
        {
            await _service.AgregarNotaAsync(_documento.Id, texto);
            NuevaNotaTexto = string.Empty;
            await RecargarAsync();
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    [RelayCommand]
    private void Volver() => _navigation.Navegar<DocumentoListViewModel>();

    /// <summary>
    /// Único punto de traducción excepción → mensaje para los comandos de esta pantalla
    /// (molde: TareaFormViewModel.ResolverMensajeError). UnauthorizedAccessException se
    /// atrapa en SILENCIO -- mismo motivo que DocumentoListViewModel.ManejarErrorAsync.
    /// </summary>
    private Task ManejarErrorAsync(Exception ex)
    {
        if (ex is UnauthorizedAccessException) return Task.CompletedTask;

        MensajeError = ex switch
        {
            ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException
                or ServidorNoDisponibleException => ex.Message,
            _ => "Ocurrió un error inesperado. Si el problema persiste, contactá a soporte.",
        };
        return Task.CompletedTask;
    }
}
