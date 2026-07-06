using System.Collections.Generic;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Movimientos;

/// <summary>
/// Formulario de registro de ENTRADA de stock: tipo fijo <see cref="TipoMovimiento.Entrada"/>,
/// motivos habilitados restringidos a Compra y Ajuste.
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
}
