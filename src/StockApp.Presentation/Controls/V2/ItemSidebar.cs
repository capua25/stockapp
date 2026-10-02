using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Optris.Icons.Avalonia;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Ítem del sidebar v2 (spec §4.1): ícono, título y pastilla cuando está activo. Hereda de Button
/// para tener Command, foco y teclado (y para que ShellMainViewGatesTests lo encuentre por la
/// identidad del Command); su StyleKey es ItemSidebar, así que los estilos "Button" viejos y v2
/// no lo alcanzan (mismo patrón que TarjetaAcceso).
/// </summary>
public class ItemSidebar : Button
{
    private Icon? _icono;

    public static readonly StyledProperty<string?> IconoProperty =
        AvaloniaProperty.Register<ItemSidebar, string?>(nameof(Icono));

    public static readonly StyledProperty<string?> TituloProperty =
        AvaloniaProperty.Register<ItemSidebar, string?>(nameof(Titulo));

    public static readonly StyledProperty<bool> EstaActivoProperty =
        AvaloniaProperty.Register<ItemSidebar, bool>(nameof(EstaActivo));

    protected override Type StyleKeyOverride => typeof(ItemSidebar);

    /// <summary>Clave de ícono Material Design, p. ej. "mdi-home".</summary>
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

    /// <summary>Pastilla activa (pseudoclase :activo). La calcula ShellMainViewModel por navegación.</summary>
    public bool EstaActivo
    {
        get => GetValue(EstaActivoProperty);
        set => SetValue(EstaActivoProperty, value);
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
        if (change.Property == EstaActivoProperty)
        {
            PseudoClasses.Set(":activo", change.GetNewValue<bool>());
        }
        else if (change.Property == IconoProperty)
        {
            AplicarIcono();
        }
    }

    // Igual que TarjetaAcceso.AplicarIcono: Optris maneja clave nula o vacía sin lanzar.
    private void AplicarIcono()
    {
        if (_icono is not null)
        {
            _icono.Value = Icono ?? string.Empty;
        }
    }
}
