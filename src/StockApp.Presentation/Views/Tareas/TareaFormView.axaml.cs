using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Tareas;

namespace StockApp.Presentation.Views.Tareas;

/// <summary>
/// A partir de la clasificación (spec 2026-09-08), el modo alta SÍ tiene combos que
/// precargar de forma asíncrona (Zona/Dimensión/Organismo/Origen vía ClasificacionPanel),
/// así que este archivo pasa a necesitar el wiring de DataContextChanged que antes no hacía
/// falta (el comentario viejo de esta clase, "sin combos que precargar", ya no es cierto) —
/// mismo criterio que GastoFormView. CargarParaCrear()/CargarParaVer() siguen siendo
/// síncronos y corren ANTES de que la navegación publique el VM como DataContext;
/// DataContextChanged solo dispara la carga ASÍNCRONA del panel de clasificación, y SOLO en
/// modo alta (en modo detalle los cinco campos son de solo lectura, no hace falta poblar
/// combos).
/// </summary>
public partial class TareaFormView : UserControl
{
    public TareaFormView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is TareaFormViewModel vm && vm.EsNuevaTarea)
                await vm.ClasificacionPanel.InicializarAsync();
        };
    }
}
