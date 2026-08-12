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

    private async Task RecargarAsync()
    {
        try
        {
            Items.Clear();
            var lista = await _adjuntos.ListarPorDocumentoAsync(_documentoId);
            foreach (var item in lista ?? Array.Empty<AdjuntoDocumentoDto>())
                Items.Add(item);
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

    [RelayCommand]
    private async Task QuitarAsync(AdjuntoDocumentoDto item)
    {
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
