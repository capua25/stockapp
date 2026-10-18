using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.ApiClient;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Documentos;

/// <summary>
/// Panel de adjuntos del documento administrativo (molde: AdjuntosPanelViewModel de
/// Finanzas). D10: reusa tal cual IServicioSeleccionArchivo/IServicioAperturaArchivo. D11(a):
/// agregar y quitar solo si el documento está activo, en ambos sentidos -- por eso
/// InicializarAsync recibe el estado del documento en vez de reconsultarlo. D11(b): quitar
/// exige Admin (documentos.administrar), agregar no (documentos.gestionar).
/// </summary>
public partial class AdjuntosDocumentoPanelViewModel : ViewModelBase
{
    private readonly IAdjuntoDocumentoService  _adjuntos;
    private readonly IServicioSeleccionArchivo _seleccion;
    private readonly IServicioAperturaArchivo  _apertura;
    private readonly IConfirmacionService      _confirmacion;
    private readonly ICurrentSession           _session;

    private int _documentoId;

    public ObservableCollection<AdjuntoDocumentoDto> Items { get; } = new();

    [ObservableProperty] private bool _puedeAgregar;
    [ObservableProperty] private bool _puedeQuitar;

    public AdjuntosDocumentoPanelViewModel(
        IAdjuntoDocumentoService adjuntos,
        IServicioSeleccionArchivo seleccion,
        IServicioAperturaArchivo apertura,
        IConfirmacionService confirmacion,
        ICurrentSession session)
    {
        _adjuntos     = adjuntos;
        _seleccion    = seleccion;
        _apertura     = apertura;
        _confirmacion = confirmacion;
        _session      = session;
    }

    public async Task InicializarAsync(int documentoId, bool documentoActivo)
    {
        _documentoId = documentoId;
        var esAdmin = _session.RolActual == RolUsuario.Admin;

        PuedeAgregar = documentoActivo;
        PuedeQuitar = documentoActivo && esAdmin;

        await RecargarAsync();
    }

    /// <summary>Texto del estado "Sin permiso" del panel cuando el servidor rechaza el listado (403).</summary>
    public const string MensajeSinPermisoAdjuntos = "No tenés permiso para ver los adjuntos.";

    /// <summary>Un servidor caído al listar se muestra en línea con "Reintentar" (<see cref="ViewModelBase.SinConexion"/>), no
    /// con el modal global: la lista vacía diría "Sin adjuntos" cuando en realidad no se pudo consultar.</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin conexión no hay lista que mostrar: al reintentar no deben verse los adjuntos de la consulta anterior.</summary>
    protected override void AlPerderConexion() => Items.Clear();

    /// <summary>Turnos de <see cref="RecargarAsync"/>: solo la última carga decide lo que se ve.</summary>
    private readonly ConsultaVigente _consultas = new();

    private async Task RecargarAsync()
    {
        var turno = _consultas.Empezar();
        try
        {
            await EjecutarCargaProtegidaAsync(async () =>
            {
                Items.Clear();
                var lista = await _adjuntos.ListarPorDocumentoAsync(_documentoId);

                if (!turno.EsLaUltima)
                    return;

                foreach (var item in lista ?? Array.Empty<AdjuntoDocumentoDto>())
                    Items.Add(item);
            }, MensajeSinPermisoAdjuntos, turno);
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    [RelayCommand]
    private async Task AgregarAsync()
    {
        var seleccionado = await _seleccion.SeleccionarArchivoAsync();
        if (seleccionado is null)
            return;

        var (nombreArchivo, contenido) = seleccionado.Value;

        try
        {
            await _adjuntos.AgregarAsync(_documentoId, nombreArchivo, contenido);
            await RecargarAsync();
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    [RelayCommand]
    private async Task VerAsync(AdjuntoDocumentoDto item)
    {
        try
        {
            var contenido = await _adjuntos.ObtenerContenidoAsync(item.Id);
            await _apertura.AbrirAsync(contenido.NombreArchivo, contenido.Contenido);
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    /// <summary>Texto de la confirmación de Quitar; el mismo que el panel de adjuntos de Finanzas.</summary>
    public static string MensajeConfirmarQuitar(string nombreArchivo) => $"¿Quitar el adjunto \"{nombreArchivo}\"?";

    [RelayCommand]
    private async Task QuitarAsync(AdjuntoDocumentoDto item)
    {
        if (!await _confirmacion.PreguntarAsync(MensajeConfirmarQuitar(item.NombreArchivo))) return;

        try
        {
            await _adjuntos.QuitarAsync(item.Id);
            await RecargarAsync();
        }
        catch (Exception ex)
        {
            await ManejarErrorAsync(ex);
        }
    }

    /// <summary>
    /// UnauthorizedAccessException se atrapa en SILENCIO -- mismo motivo que
    /// DocumentoListViewModel/DocumentoFormViewModel.ManejarErrorAsync (spec "Manejo de errores").
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
