using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Grupo colapsable del menu lateral. Hasta la tanda 5 los "grupos" eran 7 TextBlock sueltos con
/// su propio IsVisible, seguidos de botones hermanos en el mismo StackPanel.
///
/// EsVisible es "algun hijo visible", NO un permiso propio. Eso arregla un bug preexistente: el
/// header "Finanzas" estaba gateado por PuedeVerFinanzas mientras su item "Maestros de finanzas"
/// va por PuedeGestionarMaestrosFinanzas, asi que un operador con ese permiso y sin VerFinanzas
/// veia el boton sin el titulo de seccion, colgando suelto.
///
/// Ruling 6 (2026-08-19): ItemsVisibles y EsVisible ya no son un cálculo hecho una sola vez en el
/// constructor -- <see cref="ActualizarVisibilidad"/> los recalcula cuando ShellMainViewModel
/// reevalúa el EsVisible de los items tras un refresco de permisos (revocación en caliente).
/// </summary>
public partial class GrupoNavegacion : ObservableObject
{
    public GrupoNavegacion(string titulo, IReadOnlyList<ItemNavegacion> items)
    {
        Titulo = titulo;
        Items = items;
        ItemsVisibles = items.Where(i => i.EsVisible).ToList();
    }

    public string Titulo { get; }

    public IReadOnlyList<ItemNavegacion> Items { get; }

    /// <summary>Solo los items que el usuario puede ver. Es lo que se bindea al ItemsControl.</summary>
    public IReadOnlyList<ItemNavegacion> ItemsVisibles { get; private set; }

    public bool EsVisible => ItemsVisibles.Count > 0;

    [ObservableProperty]
    private bool _estaExpandido;

    /// <summary>
    /// Recalcula ItemsVisibles (y por lo tanto EsVisible, que deriva de él) contra el EsVisible
    /// actual de cada item. Llamado desde ShellMainViewModel.RecalcularVisibilidad después de
    /// reasignar el EsVisible de los items -- ItemsVisibles no es un [ObservableProperty] porque
    /// deriva de una colección de sub-objetos, no de un valor simple, así que el recálculo y el
    /// aviso de cambio son manuales.
    /// </summary>
    public void ActualizarVisibilidad()
    {
        ItemsVisibles = Items.Where(i => i.EsVisible).ToList();
        OnPropertyChanged(nameof(ItemsVisibles));
        OnPropertyChanged(nameof(EsVisible));
    }
}
