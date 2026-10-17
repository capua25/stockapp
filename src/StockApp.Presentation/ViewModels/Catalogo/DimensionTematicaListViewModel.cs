using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>Catálogo de dimensiones temáticas: lo único propio es el servicio, los textos y los valores de las columnas.</summary>
public class DimensionTematicaListViewModel : CatalogoListadoViewModel<DimensionTematica>
{
    private static readonly IReadOnlyList<ColumnaCatalogo> _columnas = Array.Empty<ColumnaCatalogo>();

    private readonly IDimensionTematicaService _service;

    public DimensionTematicaListViewModel(
        IDimensionTematicaService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
        : base(navigation, confirmacion)
    {
        _service = service;
    }

    public override ClaveCatalogo Clave => ClaveCatalogo.Dimensiones;
    public override string Titulo => "Dimensiones";
    public override string TextoNuevo => "Nueva dimensión";
    public override string TextoInactivo => "Inactiva";
    public override string TextoSinPermiso => "No tenés permiso para ver las dimensiones.";
    public override IReadOnlyList<ColumnaCatalogo> Columnas => _columnas;

    protected override Task<IReadOnlyList<DimensionTematica>> ListarAsync() => _service.ListarTodasAsync();

    protected override Task DarDeBajaAsync(DimensionTematica item) => _service.BajaLogicaAsync(item.Id);

    protected override string MensajeConfirmarBaja(DimensionTematica item) => $"¿Confirma dar de baja la dimensión \"{item.Nombre}\"?";

    protected override bool EsActivo(DimensionTematica item) => item.Activo;

    protected override IReadOnlyList<string?> Valores(DimensionTematica item) => new string?[] { item.Nombre };

    protected override void NavegarANuevo() => Navigation.Navegar<DimensionTematicaFormViewModel>();

    protected override void NavegarAEditar(DimensionTematica item)
        => Navigation.Navegar<DimensionTematicaFormViewModel>(vm => vm.CargarParaEditar(item));
}
