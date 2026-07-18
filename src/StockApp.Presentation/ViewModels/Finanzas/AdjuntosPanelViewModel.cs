using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
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

    private int? _gastoId;
    private int? _pagoGastoId;

    public ObservableCollection<AdjuntoDto> Items { get; } = new();

    [ObservableProperty]
    private bool _puedeModificar;

    public AdjuntosPanelViewModel(
        IAdjuntoService adjuntos,
        IServicioSeleccionArchivo seleccion,
        IServicioAperturaArchivo apertura,
        IConfirmacionService confirmacion)
    {
        _adjuntos = adjuntos;
        _seleccion = seleccion;
        _apertura = apertura;
        _confirmacion = confirmacion;
    }

    public async Task InicializarAsync(int? gastoId, int? pagoGastoId)
    {
        _gastoId = gastoId;
        _pagoGastoId = pagoGastoId;
        await RecargarAsync();
    }

    private async Task RecargarAsync()
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
