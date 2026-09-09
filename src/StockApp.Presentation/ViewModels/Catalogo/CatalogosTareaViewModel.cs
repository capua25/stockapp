namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Pantalla "Catálogos de tareas" (spec 2026-09-08, D2): hostea las cuatro sub-listas
/// (zonas, dimensiones temáticas, organismos responsables, orígenes de financiamiento) que
/// la vista muestra en tabs. Los formularios de alta/edición navegan a pantalla completa y
/// vuelven acá al guardar o cancelar — mismo patrón que MaestrosFinanzasViewModel.
/// </summary>
public partial class CatalogosTareaViewModel : ViewModelBase
{
    public ZonaListViewModel ZonasVm { get; }
    public DimensionTematicaListViewModel DimensionesVm { get; }
    public OrganismoResponsableListViewModel OrganismosVm { get; }
    public OrigenFinanciamientoListViewModel OrigenesVm { get; }

    public CatalogosTareaViewModel(
        ZonaListViewModel zonasVm,
        DimensionTematicaListViewModel dimensionesVm,
        OrganismoResponsableListViewModel organismosVm,
        OrigenFinanciamientoListViewModel origenesVm)
    {
        ZonasVm       = zonasVm;
        DimensionesVm = dimensionesVm;
        OrganismosVm  = organismosVm;
        OrigenesVm    = origenesVm;
    }
}
