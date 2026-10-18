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
    private readonly ICurrentSession _session;

    private int? _gastoId;
    private int? _pagoGastoId;

    /// <summary>Turnos de <see cref="RecargarAsync"/>: dos cargas pueden superponerse (el host cambia de pago mientras llega
    /// la lista del anterior) y solo la última decide lo que se ve, o los comprobantes de un pago aparecerían bajo otro.</summary>
    private readonly ConsultaVigente _consultas = new();

    /// <summary>El host lo fija cuando el dueño del panel está anulado: el panel se consulta (Ver) pero no se modifica
    /// (Agregar y Quitar ocultos), aunque el usuario tenga el permiso.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeModificar))]
    private bool _soloLectura;

    /// <summary>Texto que explica el solo lectura, debajo del encabezado del panel. Null: sin aviso.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayAvisoSoloLectura))]
    private string? _avisoSoloLectura;

    public bool HayAvisoSoloLectura => !string.IsNullOrEmpty(AvisoSoloLectura);

    public ObservableCollection<AdjuntoDto> Items { get; } = new();

    /// <summary>Permiso que habilita Agregar y Quitar: el de gastos para el panel de una factura, el de pagos para el
    /// de un pago. Null hasta <see cref="InicializarAsync"/>.</summary>
    private string? _permisoParaModificar;

    /// <summary>
    /// Gate de Agregar y Quitar. Getter sobre la sesión (no un valor calculado una vez): un 403 que cambia los permisos
    /// con el panel abierto lo re-evalúa por <see cref="ViewModelBase.NotificarPermisosActualizados"/>, que el host le
    /// reenvía (el panel no es la pantalla abierta: el shell solo le avisa al host).
    /// </summary>
    public bool PuedeModificar =>
        !SoloLectura
        && _permisoParaModificar is { } permiso
        && (_session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(permiso));

    public static string MensajeConfirmarQuitar(string nombreArchivo) => $"¿Quitar el adjunto \"{nombreArchivo}\"?";

    public AdjuntosPanelViewModel(
        IAdjuntoService adjuntos,
        IServicioSeleccionArchivo seleccion,
        IServicioAperturaArchivo apertura,
        IConfirmacionService confirmacion,
        ICurrentSession session)
    {
        _adjuntos = adjuntos;
        _seleccion = seleccion;
        _apertura = apertura;
        _confirmacion = confirmacion;
        _session = session;
    }

    public async Task InicializarAsync(int? gastoId, int? pagoGastoId)
    {
        _gastoId = gastoId;
        _pagoGastoId = pagoGastoId;

        _permisoParaModificar = gastoId is int ? Permisos.RegistrarGastos : Permisos.RegistrarPagos;
        OnPropertyChanged(nameof(PuedeModificar));

        await RecargarAsync();
    }

    /// <summary>Texto del estado "Sin permiso" del panel cuando el servidor rechaza el listado (403).</summary>
    public const string MensajeSinPermisoAdjuntos = "No tenés permiso para ver los adjuntos.";

    /// <summary>Un servidor caído al listar se muestra en línea con "Reintentar" (<see cref="ViewModelBase.SinConexion"/>), no
    /// con el modal global: la lista vacía diría "Sin adjuntos" cuando en realidad no se pudo consultar.</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin conexión no hay lista que mostrar: al reintentar no deben verse los adjuntos de la consulta anterior.</summary>
    protected override void AlPerderConexion() => Items.Clear();

    /// <summary>Reintentar vuelve a pasar por <see cref="RecargarAsync"/>, que es donde está el catch del panel: si el
    /// listado falla ahora por negocio, el error va al modal y no se escapa del comando. Además pide un turno nuevo.</summary>
    protected override Task ReintentarAsync(Func<Task> cargaFallida) => RecargarAsync();

    private async Task RecargarAsync()
    {
        var turno = _consultas.Empezar();
        try
        {
            await EjecutarCargaProtegidaAsync(async () =>
            {
                Items.Clear();
                IReadOnlyList<AdjuntoDto>? lista = _gastoId is int gastoId
                    ? await _adjuntos.ListarPorGastoAsync(gastoId)
                    : _pagoGastoId is int pagoGastoId
                        ? await _adjuntos.ListarPorPagoAsync(pagoGastoId)
                        : Array.Empty<AdjuntoDto>();

                if (!turno.EsLaUltima)
                    return;

                foreach (var item in lista ?? Array.Empty<AdjuntoDto>())
                    Items.Add(item);
            }, MensajeSinPermisoAdjuntos, turno);
        }
        catch (Exception ex)
        {
            if (turno.EsLaUltima)
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

    /// <summary>Pide confirmación antes de quitar: es una baja lógica, pero el adjunto deja de verse en la factura.</summary>
    [RelayCommand]
    private async Task QuitarAsync(AdjuntoDto item)
    {
        if (!await _confirmacion.PreguntarAsync(MensajeConfirmarQuitar(item.NombreArchivo)))
            return;

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
