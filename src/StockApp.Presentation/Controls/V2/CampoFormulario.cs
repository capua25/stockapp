using Avalonia;
using Avalonia.Controls;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Etiqueta arriba, asterisco si es requerido, control en el Content (spec §3.3 y §5.2).
/// NO intercepta la validación: el control de entrada queda como hijo directo de un
/// ContentPresenter y sigue recibiendo por selector de tipo el ErrorConverter de
/// Themes/Controls.axaml. El mensaje de error lo dibuja el DataValidationErrors del propio
/// control (estilo .ui2 DataValidationErrors de Themes/V2/ControlesV2.axaml).
/// </summary>
public class CampoFormulario : ContentControl
{
    public static readonly StyledProperty<string?> EtiquetaProperty =
        AvaloniaProperty.Register<CampoFormulario, string?>(nameof(Etiqueta));

    public static readonly StyledProperty<bool> RequeridoProperty =
        AvaloniaProperty.Register<CampoFormulario, bool>(nameof(Requerido));

    public string? Etiqueta
    {
        get => GetValue(EtiquetaProperty);
        set => SetValue(EtiquetaProperty, value);
    }

    /// <summary>Marca visual de obligatorio. No valida nada por sí solo.</summary>
    public bool Requerido
    {
        get => GetValue(RequeridoProperty);
        set => SetValue(RequeridoProperty, value);
    }
}
