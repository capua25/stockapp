using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.ViewModels.Movimientos;

/// <summary>
/// Formulario de registro de ENTRADA de stock: tipo fijo <see cref="TipoMovimiento.Entrada"/>,
/// motivos habilitados restringidos a Compra y Ajuste. Tras registrar una COMPRA ofrece el
/// paso OPCIONAL "Asociar factura" (spec Finanzas §5.1): si el usuario acepta, navega al
/// formulario de gasto precargado con el movimiento y el monto sugerido (cantidad × precio,
/// editable — la factura real puede traer fletes o redondeos). Ajustes no llevan factura.
/// </summary>
public sealed partial class EntradaRegistroViewModel : MovimientoRegistroViewModelBase
{
    private static readonly IReadOnlyList<MotivoMovimiento> _motivosDisponibles =
        new[] { MotivoMovimiento.Compra, MotivoMovimiento.Ajuste };

    public override TipoMovimiento Tipo => TipoMovimiento.Entrada;

    public override IReadOnlyList<MotivoMovimiento> MotivosDisponibles => _motivosDisponibles;

    public override string Titulo => "Registrar Entrada";

    public EntradaRegistroViewModel(
        IMovimientoStockService service,
        IProductoService productoService,
        INavigationService navigation,
        IConfirmacionService confirmacion)
        : base(service, productoService, navigation, confirmacion)
    {
        Motivo = MotivosDisponibles[0];
    }

    protected override async Task AlRegistradoAsync(MovimientoRegistradoDto registrado)
    {
        if (registrado.Motivo != MotivoMovimiento.Compra)
        {
            await base.AlRegistradoAsync(registrado);
            return;
        }

        var asociar = await Confirmacion.PreguntarAsync(
            "Entrada registrada. ¿Desea asociar una factura de proveedor a esta entrada?");
        if (!asociar)
        {
            await base.AlRegistradoAsync(registrado);
            return;
        }

        var montoSugerido = registrado.Cantidad * registrado.PrecioUnitario;
        Navigation.Navegar<GastoFormViewModel>(vm =>
            vm.CargarDesdeEntrada(registrado.MovimientoId, montoSugerido));
    }
}
