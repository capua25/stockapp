using System;
using System.Collections.ObjectModel;
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
/// Fila de solo lectura de la lista de documentos: aplana la entidad y agrega el gating de
/// acciones por transición de estado (dominio, D4) combinado con rol (spec: las acciones
/// documentos.administrar -- Anular/Reabrir -- las oculta un Operador aunque el dominio
/// permita la transición). Molde: TareaFila.PuedeCancelar.
/// </summary>
public sealed class DocumentoFila
{
    public DocumentoAdministrativo Documento { get; }
    private readonly RolUsuario _rol;

    public DocumentoFila(DocumentoAdministrativo documento, RolUsuario rol)
    {
        Documento = documento;
        _rol = rol;
    }

    public int Id => Documento.Id;
    public string Numero => Documento.Numero;
    public int Anio => Documento.Anio;
    public string TipoTexto => Documento.Tipo.ToString();
    public DateTime FechaEmision => Documento.FechaEmision;
    public string Descripcion => Documento.Descripcion;
    public string EstadoTexto => Documento.Estado.ToString();
    public string? RegistradoPorNombre => Documento.RegistradoPor?.NombreUsuario;

    // PuedeIniciar exige ADEMÁS Estado == Pendiente, no solo PuedeTransicionarA(EnProceso): en
    // la tabla del dominio (D4), Finalizado/Anulado -> EnProceso también son transiciones
    // válidas (son la reapertura), así que PuedeTransicionarA(EnProceso) da true también sobre
    // un documento cerrado. Sin este chequeo extra, el botón "Iniciar" aparecería habilitado
    // sobre un documento Finalizado/Anulado y el usuario comería el 409 que el servicio ya
    // rechaza (Task 8, guarda simétrica a la de ReabrirAsync).
    public bool PuedeIniciar          => Documento.Estado == EstadoDocumento.Pendiente && Documento.PuedeTransicionarA(EstadoDocumento.EnProceso);
    public bool PuedeVolverAPendiente => Documento.PuedeTransicionarA(EstadoDocumento.Pendiente);
    public bool PuedeFinalizar        => Documento.PuedeTransicionarA(EstadoDocumento.Finalizado);

    public bool PuedeAnular =>
        _rol == RolUsuario.Admin && Documento.PuedeTransicionarA(EstadoDocumento.Anulado);

    public bool PuedeReabrir =>
        _rol == RolUsuario.Admin && Documento.EsCerrado;
}

/// <summary>
/// Opción de filtro por tipo de documento para los ComboBox de Activos/Historial.
/// Valor=null representa "Todos" (sin filtro de tipo). Molde: OpcionTipoMovimiento
/// (MovimientoHistorialViewModel).
/// </summary>
public sealed record OpcionTipoDocumento(string Nombre, TipoDocumento? Valor);

/// <summary>
/// Opción de filtro por estado para el ComboBox de Historial. Solo ofrece los dos estados
/// cerrados (Finalizado/Anulado, D9) -- un Pendiente/EnProceso nunca aparecería en
/// ListarCerradosAsync, así que ofrecerlo como opción sería un filtro que siempre da vacío.
/// Valor=null representa "Todos" (sin filtro de estado).
/// </summary>
public sealed record OpcionEstadoDocumento(string Nombre, EstadoDocumento? Valor);

/// <summary>
/// Pantalla "Documentos administrativos": dos solapas, Activos (Pendiente/EnProceso) e
/// Historial (Finalizado/Anulado, D9). El historial se carga perezoso -- recién al abrir la
/// solapa, no en CargarAsync() inicial -- y exige año (el servidor lo rechaza si viene nulo,
/// D9); la UI precarga el año actual como valor inicial del filtro.
/// La vista dispara CargarAsync() vía DataContextChanged (convención del proyecto).
/// </summary>
public partial class DocumentoListViewModel : ViewModelBase
{
    private readonly IDocumentoAdministrativoService _service;
    private readonly ICurrentSession                 _session;
    private readonly INavigationService               _navigation;
    private readonly IConfirmacionService              _confirmacion;

    private bool _historialCargado;

    [ObservableProperty] private TipoDocumento? _filtroActivosTipo;
    [ObservableProperty] private string? _filtroActivosTexto;

    [ObservableProperty] private TipoDocumento? _filtroHistorialTipo;
    [ObservableProperty] private int? _filtroHistorialAnio = DateTime.UtcNow.Year;
    [ObservableProperty] private string? _filtroHistorialTexto;
    [ObservableProperty] private EstadoDocumento? _filtroHistorialEstado;

    /// <summary>Opción elegida en el ComboBox de Tipo de la solapa Activos (Valor=null = "Todos").</summary>
    [ObservableProperty] private OpcionTipoDocumento? _filtroActivosTipoSeleccionado;

    /// <summary>Opción elegida en el ComboBox de Tipo de la solapa Historial (Valor=null = "Todos").</summary>
    [ObservableProperty] private OpcionTipoDocumento? _filtroHistorialTipoSeleccionado;

    /// <summary>Opción elegida en el ComboBox de Estado de la solapa Historial (Valor=null = "Todos").</summary>
    [ObservableProperty] private OpcionEstadoDocumento? _filtroHistorialEstadoSeleccionado;

    public ObservableCollection<DocumentoFila> Activos { get; } = new();
    public ObservableCollection<DocumentoFila> Historial { get; } = new();

    /// <summary>Opciones fijas para los ComboBox de filtro por tipo ("Todos" + los 3 tipos del dominio).</summary>
    public ObservableCollection<OpcionTipoDocumento> TiposDisponibles { get; } = new()
    {
        new OpcionTipoDocumento("Todos", null),
        new OpcionTipoDocumento("Expediente", TipoDocumento.Expediente),
        new OpcionTipoDocumento("Oficio", TipoDocumento.Oficio),
        new OpcionTipoDocumento("Suministro", TipoDocumento.Suministro),
    };

    /// <summary>Opciones fijas para el ComboBox de filtro por estado de Historial ("Todos" + los 2 estados cerrados).</summary>
    public ObservableCollection<OpcionEstadoDocumento> EstadosDisponibles { get; } = new()
    {
        new OpcionEstadoDocumento("Todos", null),
        new OpcionEstadoDocumento("Finalizado", EstadoDocumento.Finalizado),
        new OpcionEstadoDocumento("Anulado", EstadoDocumento.Anulado),
    };

    public DocumentoListViewModel(
        IDocumentoAdministrativoService service, ICurrentSession session,
        INavigationService navigation, IConfirmacionService confirmacion)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;

        _filtroActivosTipoSeleccionado = TiposDisponibles[0];
        _filtroHistorialTipoSeleccionado = TiposDisponibles[0];
        _filtroHistorialEstadoSeleccionado = EstadosDisponibles[0];
    }

    partial void OnFiltroActivosTipoSeleccionadoChanged(OpcionTipoDocumento? value)
        => FiltroActivosTipo = value?.Valor;

    partial void OnFiltroHistorialTipoSeleccionadoChanged(OpcionTipoDocumento? value)
        => FiltroHistorialTipo = value?.Valor;

    partial void OnFiltroHistorialEstadoSeleccionadoChanged(OpcionEstadoDocumento? value)
        => FiltroHistorialEstado = value?.Valor;

    private RolUsuario RolActualODefault => _session.RolActual ?? RolUsuario.Operador;

    public async Task CargarAsync()
    {
        try
        {
            var filtro = new FiltroDocumentos(FiltroActivosTipo, null, FiltroActivosTexto, null);
            var documentos = await _service.ListarActivosAsync(filtro);
            var rol = RolActualODefault;

            Activos.Clear();
            foreach (var doc in documentos)
                Activos.Add(new DocumentoFila(doc, rol));
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    /// <summary>
    /// D9: recarga el historial SIEMPRE que se invoca (a diferencia de AbrirHistorialCommand,
    /// que solo carga la primera vez) -- es lo que dispara el botón "Buscar" del filtro propio
    /// del historial cuando el usuario cambia año/tipo/texto/estado.
    /// </summary>
    public async Task CargarHistorialAsync()
    {
        try
        {
            var filtro = new FiltroDocumentos(FiltroHistorialTipo, FiltroHistorialAnio, FiltroHistorialTexto, FiltroHistorialEstado);
            var documentos = await _service.ListarHistorialAsync(filtro);
            var rol = RolActualODefault;

            Historial.Clear();
            foreach (var doc in documentos)
                Historial.Add(new DocumentoFila(doc, rol));

            _historialCargado = true;
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    /// <summary>
    /// D9 (carga perezosa): la vista invoca este comando al seleccionar la solapa Historial.
    /// Solo consulta al servicio la primera vez -- volver a seleccionar la solapa no repite
    /// la consulta (usar CargarHistorialAsync() directamente para forzar un refresco).
    /// </summary>
    [RelayCommand]
    private async Task AbrirHistorial()
    {
        if (_historialCargado) return;
        await CargarHistorialAsync();
    }

    /// <summary>
    /// Botón "Buscar" del filtro propio del historial (Task 22): a diferencia de
    /// AbrirHistorialCommand (carga perezosa, una sola vez), este comando envuelve
    /// CargarHistorialAsync() -- que queda público y sin [RelayCommand] porque también lo
    /// invocan AbrirHistorialCommand y los tests -- para exponer un ICommand bindeable que
    /// siempre vuelve a consultar al servidor con el filtro actual.
    /// </summary>
    [RelayCommand]
    private async Task BuscarHistorial() => await CargarHistorialAsync();

    /// <summary>
    /// Botón "Buscar" de la solapa Activos (I2 del review final): FiltroActivosTexto/Tipo no
    /// tenían disparador propio -- el único punto de recarga era DataContextChanged, que corre
    /// una sola vez al entrar a la pantalla. Molde: BuscarHistorialCommand.
    /// </summary>
    [RelayCommand]
    private async Task BuscarActivos() => await CargarAsync();

    [RelayCommand]
    private void Nuevo() => _navigation.Navegar<DocumentoFormViewModel>(vm => vm.CargarParaCrear());

    [RelayCommand]
    private void VerDetalle(DocumentoFila fila)
        => _navigation.Navegar<DocumentoFormViewModel>(vm => _ = vm.CargarParaVerAsync(fila.Documento));

    [RelayCommand]
    private async Task Iniciar(DocumentoFila fila)
    {
        try { await _service.IniciarProcesoAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task VolverAPendiente(DocumentoFila fila)
    {
        try { await _service.VolverAPendienteAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    [RelayCommand]
    private async Task Finalizar(DocumentoFila fila)
    {
        // M9: Finalizar es la única acción de esta pantalla que cierra un documento (Anular
        // vive en el formulario, D... ver comentario del botón en el XAML) -- sin invalidar
        // acá, _historialCargado seguía en true y el documento recién cerrado no aparecía en
        // Historial hasta apretar "Buscar" a mano.
        try { await _service.FinalizarAsync(fila.Id); await CargarAsync(); _historialCargado = false; }
        catch (Exception ex) { await ManejarErrorAsync(ex); }
    }

    /// <summary>
    /// Único punto de traducción excepción → mensaje para todos los comandos de esta pantalla
    /// (molde: TareaListViewModel.ManejarErrorAsync). A diferencia de Tareas,
    /// UnauthorizedAccessException se atrapa en SILENCIO (spec "Manejo de errores"): el
    /// manejador central del 403 (AuthTokenHandler + AccesoRevocado, App.axaml.cs) ya muestra
    /// el aviso y refresca permisos -- informarlo también acá vuelve el doble aviso que se
    /// corrigió en el commit 093fc7c.
    /// </summary>
    private async Task ManejarErrorAsync(Exception ex)
    {
        if (ex is UnauthorizedAccessException) return;

        var mensaje = ex switch
        {
            ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException
                or ServidorNoDisponibleException => ex.Message,
            _ => "Ocurrió un error inesperado. Si el problema persiste, contactá a soporte.",
        };
        await _confirmacion.InformarAsync(mensaje);
    }
}
