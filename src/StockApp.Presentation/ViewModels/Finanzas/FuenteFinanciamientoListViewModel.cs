using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Catálogo de fuentes de financiamiento: lo único propio es el servicio, los textos y los valores de las columnas.</summary>
public class FuenteFinanciamientoListViewModel : CatalogoListadoViewModel<FuenteFinanciamiento>
{
    private static readonly IReadOnlyList<ColumnaCatalogo> _columnas = Array.Empty<ColumnaCatalogo>();

    private readonly IFuenteFinanciamientoService _service;

    public FuenteFinanciamientoListViewModel(
        IFuenteFinanciamientoService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
        : base(navigation, confirmacion)
    {
        _service = service;
    }

    public override ClaveCatalogo Clave => ClaveCatalogo.FuentesFinanciamiento;
    public override string Titulo => "Fuentes de financiamiento";
    public override string TextoNuevo => "Nueva fuente";
    public override string TextoInactivo => "Inactiva";
    public override string TextoSinPermiso => "No tenés permiso para ver las fuentes de financiamiento.";
    public override IReadOnlyList<ColumnaCatalogo> Columnas => _columnas;

    protected override Task<IReadOnlyList<FuenteFinanciamiento>> ListarAsync() => _service.ListarTodasAsync();

    protected override Task DarDeBajaAsync(FuenteFinanciamiento item) => _service.BajaLogicaAsync(item.Id);

    protected override string MensajeConfirmarBaja(FuenteFinanciamiento item) => $"¿Confirma dar de baja la fuente de financiamiento \"{item.Nombre}\"?";

    protected override bool EsActivo(FuenteFinanciamiento item) => item.Activo;

    protected override IReadOnlyList<string?> Valores(FuenteFinanciamiento item) => new string?[] { item.Nombre };

    protected override void NavegarANuevo() => Navigation.Navegar<FuenteFinanciamientoFormViewModel>();

    protected override void NavegarAEditar(FuenteFinanciamiento item)
        => Navigation.Navegar<FuenteFinanciamientoFormViewModel>(vm => vm.CargarParaEditar(item));
}
