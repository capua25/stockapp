using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>Catálogo de zonas: lo único propio es el servicio, los textos y los valores de las columnas.</summary>
public class ZonaListViewModel : CatalogoListadoViewModel<Zona>
{
    private static readonly IReadOnlyList<ColumnaCatalogo> _columnas = Array.Empty<ColumnaCatalogo>();

    private readonly IZonaService _service;

    public ZonaListViewModel(
        IZonaService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
        : base(navigation, confirmacion)
    {
        _service = service;
    }

    public override ClaveCatalogo Clave => ClaveCatalogo.Zonas;
    public override string Titulo => "Zonas";
    public override string TextoNuevo => "Nueva zona";
    public override string TextoInactivo => "Inactiva";
    public override string TextoSinPermiso => "No tenés permiso para ver las zonas.";
    public override string PieSingular => "zona";
    public override string PiePlural => "zonas";
    public override IReadOnlyList<ColumnaCatalogo> Columnas => _columnas;

    protected override Task<IReadOnlyList<Zona>> ListarAsync() => _service.ListarTodasAsync();

    protected override Task DarDeBajaAsync(Zona item) => _service.BajaLogicaAsync(item.Id);

    protected override string MensajeConfirmarBaja(Zona item) => $"¿Confirma dar de baja la zona \"{item.Nombre}\"?";

    protected override bool EsActivo(Zona item) => item.Activo;

    protected override IReadOnlyList<string?> Valores(Zona item) => new string?[] { item.Nombre };

    protected override void NavegarANuevo() => Navigation.Navegar<ZonaFormViewModel>();

    protected override void NavegarAEditar(Zona item)
        => Navigation.Navegar<ZonaFormViewModel>(vm => vm.CargarParaEditar(item));
}
