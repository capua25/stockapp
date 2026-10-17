using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Catálogo de Líneas POA: lo único propio es el servicio, los textos y los valores de las columnas.</summary>
public class LineaPoaListViewModel : CatalogoListadoViewModel<LineaPoa>
{
    private static readonly IReadOnlyList<ColumnaCatalogo> _columnas = [new("PROGRAMA"), new("EJERCICIO")];

    private readonly ILineaPoaService _service;

    public LineaPoaListViewModel(
        ILineaPoaService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
        : base(navigation, confirmacion)
    {
        _service = service;
    }

    public override ClaveCatalogo Clave => ClaveCatalogo.LineasPoa;
    public override string Titulo => "Líneas POA";
    public override string TextoNuevo => "Nueva línea POA";
    public override string TextoInactivo => "Inactiva";
    public override string TextoSinPermiso => "No tenés permiso para ver las líneas POA.";
    public override IReadOnlyList<ColumnaCatalogo> Columnas => _columnas;

    protected override Task<IReadOnlyList<LineaPoa>> ListarAsync() => _service.ListarTodasAsync();

    protected override Task DarDeBajaAsync(LineaPoa item) => _service.BajaLogicaAsync(item.Id);

    protected override string MensajeConfirmarBaja(LineaPoa item) => $"¿Confirma dar de baja la línea POA \"{item.Nombre}\" ({item.Ejercicio})?";

    protected override bool EsActivo(LineaPoa item) => item.Activo;

    protected override IReadOnlyList<string?> Valores(LineaPoa item) => new string?[] { item.Nombre, item.Programa, item.Ejercicio.ToString(CultureInfo.InvariantCulture) };

    protected override void NavegarANuevo() => Navigation.Navegar<LineaPoaFormViewModel>();

    protected override void NavegarAEditar(LineaPoa item)
        => Navigation.Navegar<LineaPoaFormViewModel>(vm => vm.CargarParaEditar(item));
}
