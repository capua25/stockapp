using System.Collections.Generic;

namespace StockApp.Presentation.ViewModels.Administracion;

/// <summary>Un encabezado de sección del panel de permisos (Task 3/4/6) con sus ítems
/// -- "Catálogo", "Finanzas", "Tareas y reportes", "Documentos". Construido una única vez en
/// el constructor de PanelPermisosViewModel a partir de CatalogoPermisosPanel, agrupando en
/// el orden de declaración del catálogo (nunca alfabético).</summary>
public class GrupoPermisos
{
    public string Nombre { get; }
    public IReadOnlyList<ItemPermiso> Items { get; }

    public GrupoPermisos(string nombre, IReadOnlyList<ItemPermiso> items)
    {
        Nombre = nombre;
        Items = items;
    }
}
