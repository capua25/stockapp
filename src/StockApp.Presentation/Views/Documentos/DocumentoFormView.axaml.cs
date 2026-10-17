using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Documentos;

namespace StockApp.Presentation.Views.Documentos;

public partial class DocumentoFormView : UserControl
{
    public DocumentoFormView()
    {
        InitializeComponent();

        // Salir de una caja sin completarla cuenta como tocarla: su error aparece ahí y no recién al guardar (que con el campo
        // vacío está deshabilitado, así que nunca se llegaría a ver).
        CajaNumero.LostFocus += (_, _) => (DataContext as DocumentoFormViewModel)?.MarcarTocado(nameof(DocumentoFormViewModel.Numero));
        CajaDescripcion.LostFocus += (_, _) => (DataContext as DocumentoFormViewModel)?.MarcarTocado(nameof(DocumentoFormViewModel.Descripcion));
    }
}
