using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StockApp.Presentation.VisualTests.Catalogo;

public partial class CatalogoComponentesView : UserControl
{
    public CatalogoComponentesView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Estado de error fijo para mostrar un campo inválido. Se setea en Loaded (después de que
        // los bindings resolvieron: una actualización exitosa del binding limpiaría el error) y
        // directo, no por validación del VM: el catálogo valida CÓMO se ve el seam, no el seam.
        DataValidationErrors.SetErrors(CampoCodigo, new object[] { "El código ya existe" });
    }
}
