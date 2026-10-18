using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>Catálogo de proveedores: lo único propio es el servicio, los textos y los valores de las columnas.</summary>
public class ProveedorListViewModel : CatalogoListadoViewModel<Proveedor>
{
    private static readonly IReadOnlyList<ColumnaCatalogo> _columnas = [new("TELÉFONO"), new("EMAIL"), new("DIRECCIÓN"), new("NOTAS")];

    private readonly IProveedorService _service;

    public ProveedorListViewModel(
        IProveedorService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
        : base(navigation, confirmacion)
    {
        _service = service;
    }

    public override ClaveCatalogo Clave => ClaveCatalogo.Proveedores;
    public override string Titulo => "Proveedores";
    public override string TextoNuevo => "Nuevo proveedor";
    public override string TextoInactivo => "Inactivo";
    public override string TextoSinPermiso => "No tenés permiso para ver los proveedores.";
    public override string PieSingular => "proveedor";
    public override string PiePlural => "proveedores";
    public override IReadOnlyList<ColumnaCatalogo> Columnas => _columnas;

    protected override Task<IReadOnlyList<Proveedor>> ListarAsync() => _service.ListarTodosAsync();

    protected override Task DarDeBajaAsync(Proveedor item) => _service.BajaLogicaAsync(item.Id);

    protected override string MensajeConfirmarBaja(Proveedor item) => $"¿Confirma dar de baja el proveedor \"{item.Nombre}\"?";

    protected override bool EsActivo(Proveedor item) => item.Activo;

    protected override IReadOnlyList<string?> Valores(Proveedor item) => new string?[] { item.Nombre, item.Telefono, item.Email, item.Direccion, item.Notas };

    protected override void NavegarANuevo() => Navigation.Navegar<ProveedorFormViewModel>();

    protected override void NavegarAEditar(Proveedor item)
        => Navigation.Navegar<ProveedorFormViewModel>(vm => vm.CargarParaEditar(item));
}
