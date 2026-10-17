using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>Catálogo de categorías: lo único propio es el servicio, los textos y los valores de las columnas.</summary>
public class CategoriaListViewModel : CatalogoListadoViewModel<Categoria>
{
    private static readonly IReadOnlyList<ColumnaCatalogo> _columnas = Array.Empty<ColumnaCatalogo>();

    private readonly ICategoriaService _service;

    public CategoriaListViewModel(
        ICategoriaService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
        : base(navigation, confirmacion)
    {
        _service = service;
    }

    public override ClaveCatalogo Clave => ClaveCatalogo.Categorias;
    public override string Titulo => "Categorías";
    public override string TextoNuevo => "Nueva categoría";
    public override string TextoInactivo => "Inactiva";
    public override string TextoSinPermiso => "No tenés permiso para ver las categorías.";
    public override IReadOnlyList<ColumnaCatalogo> Columnas => _columnas;

    protected override Task<IReadOnlyList<Categoria>> ListarAsync() => _service.ListarTodasAsync();

    protected override Task DarDeBajaAsync(Categoria item) => _service.BajaLogicaAsync(item.Id);

    protected override string MensajeConfirmarBaja(Categoria item) => $"¿Confirma dar de baja la categoría \"{item.Nombre}\"?";

    protected override bool EsActivo(Categoria item) => item.Activo;

    protected override IReadOnlyList<string?> Valores(Categoria item) => new string?[] { item.Nombre };

    protected override void NavegarANuevo() => Navigation.Navegar<CategoriaFormViewModel>();

    protected override void NavegarAEditar(Categoria item)
        => Navigation.Navegar<CategoriaFormViewModel>(vm => vm.CargarParaEditar(item));
}
