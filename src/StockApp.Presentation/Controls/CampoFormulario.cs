using Avalonia;
using Avalonia.Controls;

namespace StockApp.Presentation.Controls;

/// <summary>
/// Etiqueta mas control de entrada. Hereda de ContentControl: el control de entrada va en el
/// Content y se renderiza en un ContentPresenter, asi sigue recibiendo los Setter globales de
/// Themes/Controls.axaml por selector de tipo.
///
/// IMPORTANTE: este componente NO intercepta la validacion. Controls.axaml define
/// (DataValidationErrors.ErrorConverter) para TextBox (linea 185) y ComboBox (linea 203), que es
/// el blindaje de toda la app contra excepciones crudas de .NET llegando a la UI. No definir un
/// ErrorTemplate propio aca ni envolver el control en nada que rompa el selector de tipo.
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

    /// <summary>Marca visualmente el campo como obligatorio. No valida nada por si solo.</summary>
    public bool Requerido
    {
        get => GetValue(RequeridoProperty);
        set => SetValue(RequeridoProperty, value);
    }
}
