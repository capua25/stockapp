using CommunityToolkit.Mvvm.ComponentModel;

namespace StockApp.Presentation.ViewModels.Administracion;

/// <summary>Un checkbox del panel de permisos (Tasks 3/4/6): un permiso configurable, su
/// etiqueta en pantalla, y si el Admin lo tildó. Independiente de sus vecinos -- ya no hay
/// checkboxes compuestos ni efectos laterales entre ítems (esa protección la da
/// UsuarioService.GuardarPermisosAsync, validando PermisoDependencias.Requisitos).</summary>
public partial class ItemPermiso : ObservableObject
{
    public string Clave { get; }
    public string Etiqueta { get; }

    [ObservableProperty] private bool _seleccionado;

    /// <summary>Aviso NO bloqueante de una dependencia BLANDA (paso 5 del refactor;
    /// PermisoDependencias.Recomendados) -- null cuando no aplica. Lo calcula y asigna
    /// PanelPermisosViewModel.RecalcularAdvertencias(), nunca este ítem: el aviso depende del
    /// estado de OTRO ItemPermiso (el recomendado), algo que este objeto no puede ver por sí
    /// solo.</summary>
    [ObservableProperty] private string? _advertencia;

    public ItemPermiso(string clave, string etiqueta)
    {
        Clave = clave;
        Etiqueta = etiqueta;
    }
}
