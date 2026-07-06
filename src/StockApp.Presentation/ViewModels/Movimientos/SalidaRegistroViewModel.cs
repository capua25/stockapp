using System.Collections.Generic;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Movimientos;

/// <summary>
/// Formulario de registro de SALIDA de stock: tipo fijo <see cref="TipoMovimiento.Salida"/>,
/// motivos habilitados restringidos a Venta, Merma y Ajuste.
/// </summary>
public sealed partial class SalidaRegistroViewModel : MovimientoRegistroViewModelBase
{
    private static readonly IReadOnlyList<MotivoMovimiento> _motivosDisponibles =
        new[] { MotivoMovimiento.Venta, MotivoMovimiento.Merma, MotivoMovimiento.Ajuste };

    public override TipoMovimiento Tipo => TipoMovimiento.Salida;

    public override IReadOnlyList<MotivoMovimiento> MotivosDisponibles => _motivosDisponibles;

    public override string Titulo => "Registrar Salida";

    public SalidaRegistroViewModel(
        IMovimientoStockService service,
        IProductoService productoService,
        INavigationService navigation,
        IConfirmacionService confirmacion)
        : base(service, productoService, navigation, confirmacion)
    {
        Motivo = MotivosDisponibles[0];
    }
}
