using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Optris.Icons.Avalonia;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Acceso rápido de Inicio y tarjeta del hub de Reportes (spec §3.3, §4.2 y §5.3). Hereda de
/// Button para tener Command, foco y teclado; su StyleKey es TarjetaAcceso, así que los estilos
/// "Button" (viejos y v2) no la alcanzan.
/// </summary>
public class TarjetaAcceso : Button
{
    private Icon? _icono;

    public static readonly StyledProperty<string?> IconoProperty =
        AvaloniaProperty.Register<TarjetaAcceso, string?>(nameof(Icono));

    public static readonly StyledProperty<string?> TituloProperty =
        AvaloniaProperty.Register<TarjetaAcceso, string?>(nameof(Titulo));

    public static readonly StyledProperty<string?> DescripcionProperty =
        AvaloniaProperty.Register<TarjetaAcceso, string?>(nameof(Descripcion));

    protected override Type StyleKeyOverride => typeof(TarjetaAcceso);

    /// <summary>Clave de ícono Material Design, p. ej. "mdi-package-variant". Null: sin ícono.</summary>
    public string? Icono
    {
        get => GetValue(IconoProperty);
        set => SetValue(IconoProperty, value);
    }

    public string? Titulo
    {
        get => GetValue(TituloProperty);
        set => SetValue(TituloProperty, value);
    }

    public string? Descripcion
    {
        get => GetValue(DescripcionProperty);
        set => SetValue(DescripcionProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _icono = e.NameScope.Find<Icon>("PART_Icono");
        AplicarIcono();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconoProperty)
        {
            AplicarIcono();
        }
    }

    /// <summary>
    /// El Icon de Optris resuelve el ícono en el proveedor apenas cambia Value y lanza con una
    /// clave nula o vacía (IconImage.HandleValueChanged): por eso Value se asigna desde acá y
    /// solo si hay clave, en vez de un TemplateBinding directo.
    /// </summary>
    private void AplicarIcono()
    {
        if (_icono is null)
        {
            return;
        }

        var hayIcono = !string.IsNullOrEmpty(Icono);
        _icono.IsVisible = hayIcono;
        if (hayIcono)
        {
            _icono.Value = Icono!;
        }
    }
}
