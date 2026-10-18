using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>Catálogo de orígenes de financiamiento: lo único propio es el servicio, los textos y los valores de las columnas.</summary>
public class OrigenFinanciamientoListViewModel : CatalogoListadoViewModel<OrigenFinanciamiento>
{
    private static readonly IReadOnlyList<ColumnaCatalogo> _columnas = Array.Empty<ColumnaCatalogo>();

    private readonly IOrigenFinanciamientoService _service;

    public OrigenFinanciamientoListViewModel(
        IOrigenFinanciamientoService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
        : base(navigation, confirmacion)
    {
        _service = service;
    }

    public override ClaveCatalogo Clave => ClaveCatalogo.Origenes;
    public override string Titulo => "Orígenes de financiamiento";
    public override string TextoNuevo => "Nuevo origen";
    public override string TextoInactivo => "Inactivo";
    public override string TextoSinPermiso => "No tenés permiso para ver los orígenes de financiamiento.";
    public override string PieSingular => "origen";
    public override string PiePlural => "orígenes";
    public override IReadOnlyList<ColumnaCatalogo> Columnas => _columnas;

    protected override Task<IReadOnlyList<OrigenFinanciamiento>> ListarAsync() => _service.ListarTodasAsync();

    protected override Task DarDeBajaAsync(OrigenFinanciamiento item) => _service.BajaLogicaAsync(item.Id);

    protected override string MensajeConfirmarBaja(OrigenFinanciamiento item) => $"¿Confirma dar de baja el origen de financiamiento \"{item.Nombre}\"?";

    protected override bool EsActivo(OrigenFinanciamiento item) => item.Activo;

    protected override IReadOnlyList<string?> Valores(OrigenFinanciamiento item) => new string?[] { item.Nombre };

    protected override void NavegarANuevo() => Navigation.Navegar<OrigenFinanciamientoFormViewModel>();

    protected override void NavegarAEditar(OrigenFinanciamiento item)
        => Navigation.Navegar<OrigenFinanciamientoFormViewModel>(vm => vm.CargarParaEditar(item));
}
