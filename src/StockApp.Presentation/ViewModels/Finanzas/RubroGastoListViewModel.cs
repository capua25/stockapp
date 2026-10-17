using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Catálogo de rubros de gasto: lo único propio es el servicio, los textos y los valores de las columnas.</summary>
public class RubroGastoListViewModel : CatalogoListadoViewModel<RubroGasto>
{
    private static readonly IReadOnlyList<ColumnaCatalogo> _columnas = [new("CÓDIGO")];

    private readonly IRubroGastoService _service;

    public RubroGastoListViewModel(
        IRubroGastoService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
        : base(navigation, confirmacion)
    {
        _service = service;
    }

    public override ClaveCatalogo Clave => ClaveCatalogo.RubrosGasto;
    public override string Titulo => "Rubros de gasto";
    public override string TextoNuevo => "Nuevo rubro";
    public override string TextoInactivo => "Inactivo";
    public override string TextoSinPermiso => "No tenés permiso para ver los rubros de gasto.";
    public override IReadOnlyList<ColumnaCatalogo> Columnas => _columnas;

    protected override Task<IReadOnlyList<RubroGasto>> ListarAsync() => _service.ListarTodosAsync();

    protected override Task DarDeBajaAsync(RubroGasto item) => _service.BajaLogicaAsync(item.Id);

    protected override string MensajeConfirmarBaja(RubroGasto item) => $"¿Confirma dar de baja el rubro \"{item.Nombre}\" (código {item.Codigo})?";

    protected override bool EsActivo(RubroGasto item) => item.Activo;

    protected override IReadOnlyList<string?> Valores(RubroGasto item) => new string?[] { item.Nombre, item.Codigo.ToString(CultureInfo.InvariantCulture) };

    protected override void NavegarANuevo() => Navigation.Navegar<RubroGastoFormViewModel>();

    protected override void NavegarAEditar(RubroGasto item)
        => Navigation.Navegar<RubroGastoFormViewModel>(vm => vm.CargarParaEditar(item));
}
