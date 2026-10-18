using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>Catálogo de unidades de medida: lo único propio es el servicio, los textos y los valores de las columnas.</summary>
public class UnidadMedidaListViewModel : CatalogoListadoViewModel<UnidadMedida>
{
    private static readonly IReadOnlyList<ColumnaCatalogo> _columnas = [new("ABREVIATURA")];

    private readonly IUnidadMedidaService _service;

    public UnidadMedidaListViewModel(
        IUnidadMedidaService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
        : base(navigation, confirmacion)
    {
        _service = service;
    }

    public override ClaveCatalogo Clave => ClaveCatalogo.UnidadesMedida;
    public override string Titulo => "Unidades de medida";
    public override string TextoNuevo => "Nueva unidad";
    public override string TextoInactivo => "Inactiva";
    public override string TextoSinPermiso => "No tenés permiso para ver las unidades de medida.";
    public override string PieSingular => "unidad de medida";
    public override string PiePlural => "unidades de medida";
    public override IReadOnlyList<ColumnaCatalogo> Columnas => _columnas;

    protected override Task<IReadOnlyList<UnidadMedida>> ListarAsync() => _service.ListarTodasAsync();

    protected override Task DarDeBajaAsync(UnidadMedida item) => _service.BajaLogicaAsync(item.Id);

    protected override string MensajeConfirmarBaja(UnidadMedida item) => $"¿Confirma dar de baja la unidad de medida \"{item.Nombre}\"?";

    protected override bool EsActivo(UnidadMedida item) => item.Activo;

    protected override IReadOnlyList<string?> Valores(UnidadMedida item) => new string?[] { item.Nombre, item.Abreviatura };

    protected override void NavegarANuevo() => Navigation.Navegar<UnidadMedidaFormViewModel>();

    protected override void NavegarAEditar(UnidadMedida item)
        => Navigation.Navegar<UnidadMedidaFormViewModel>(vm => vm.CargarParaEditar(item));
}
