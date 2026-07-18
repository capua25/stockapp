using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Panel reusable de adjuntos, embebido en GastoFormViewModel (adjuntos del gasto) y
/// PagosGastoViewModel (adjuntos del pago seleccionado). Exactamente uno de GastoId/
/// PagoGastoId está seteado tras InicializarAsync — igual que la invariante XOR de Adjunto.
/// </summary>
public partial class AdjuntosPanelViewModel : ViewModelBase
{
    private readonly IAdjuntoService _adjuntos;
    private readonly IServicioSeleccionArchivo _seleccion;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly IConfirmacionService _confirmacion;
    private readonly IAuthorizationService _authorization;
    private readonly ICurrentSession _session;

    private int? _gastoId;
    private int? _pagoGastoId;

    public ObservableCollection<AdjuntoDto> Items { get; } = new();

    [ObservableProperty]
    private bool _puedeModificar;

    public AdjuntosPanelViewModel(
        IAdjuntoService adjuntos,
        IServicioSeleccionArchivo seleccion,
        IServicioAperturaArchivo apertura,
        IConfirmacionService confirmacion,
        IAuthorizationService authorization,
        ICurrentSession session)
    {
        _adjuntos = adjuntos;
        _seleccion = seleccion;
        _apertura = apertura;
        _confirmacion = confirmacion;
        _authorization = authorization;
        _session = session;
    }

    public async Task InicializarAsync(int? gastoId, int? pagoGastoId)
    {
        _gastoId = gastoId;
        _pagoGastoId = pagoGastoId;

        var accion = gastoId is int ? Permisos.RegistrarGastos : Permisos.RegistrarPagos;
        PuedeModificar = _session.RolActual is RolUsuario rol && _authorization.TienePermiso(rol, accion);

        await RecargarAsync();
    }

    private async Task RecargarAsync()
    {
        try
        {
            Items.Clear();
            IReadOnlyList<AdjuntoDto>? lista = _gastoId is int gastoId
                ? await _adjuntos.ListarPorGastoAsync(gastoId)
                : _pagoGastoId is int pagoGastoId
                    ? await _adjuntos.ListarPorPagoAsync(pagoGastoId)
                    : Array.Empty<AdjuntoDto>();

            foreach (var item in lista ?? Array.Empty<AdjuntoDto>())
                Items.Add(item);
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
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
            if (_gastoId is int gastoId)
                await _adjuntos.AgregarAGastoAsync(gastoId, nombreArchivo, contenido);
            else if (_pagoGastoId is int pagoGastoId)
                await _adjuntos.AgregarAPagoAsync(pagoGastoId, nombreArchivo, contenido);

            await RecargarAsync();
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand]
    private async Task VerAsync(AdjuntoDto item)
    {
        try
        {
            var contenido = await _adjuntos.ObtenerContenidoAsync(item.Id);
            await _apertura.AbrirAsync(contenido.NombreArchivo, contenido.Contenido);
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand]
    private async Task QuitarAsync(AdjuntoDto item)
    {
        try
        {
            await _adjuntos.QuitarAsync(item.Id);
            await RecargarAsync();
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
}
