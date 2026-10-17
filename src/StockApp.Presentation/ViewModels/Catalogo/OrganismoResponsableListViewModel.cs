using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>Catálogo de organismos responsables: lo único propio es el servicio, los textos y los valores de las columnas.</summary>
public class OrganismoResponsableListViewModel : CatalogoListadoViewModel<OrganismoResponsable>
{
    private static readonly IReadOnlyList<ColumnaCatalogo> _columnas = Array.Empty<ColumnaCatalogo>();

    private readonly IOrganismoResponsableService _service;

    public OrganismoResponsableListViewModel(
        IOrganismoResponsableService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
        : base(navigation, confirmacion)
    {
        _service = service;
    }

    public override ClaveCatalogo Clave => ClaveCatalogo.Organismos;
    public override string Titulo => "Organismos responsables";
    public override string TextoNuevo => "Nuevo organismo";
    public override string TextoInactivo => "Inactivo";
    public override string TextoSinPermiso => "No tenés permiso para ver los organismos responsables.";
    public override IReadOnlyList<ColumnaCatalogo> Columnas => _columnas;

    protected override Task<IReadOnlyList<OrganismoResponsable>> ListarAsync() => _service.ListarTodasAsync();

    protected override Task DarDeBajaAsync(OrganismoResponsable item) => _service.BajaLogicaAsync(item.Id);

    protected override string MensajeConfirmarBaja(OrganismoResponsable item) => $"¿Confirma dar de baja el organismo \"{item.Nombre}\"?";

    protected override bool EsActivo(OrganismoResponsable item) => item.Activo;

    protected override IReadOnlyList<string?> Valores(OrganismoResponsable item) => new string?[] { item.Nombre };

    protected override void NavegarANuevo() => Navigation.Navegar<OrganismoResponsableFormViewModel>();

    protected override void NavegarAEditar(OrganismoResponsable item)
        => Navigation.Navegar<OrganismoResponsableFormViewModel>(vm => vm.CargarParaEditar(item));
}
