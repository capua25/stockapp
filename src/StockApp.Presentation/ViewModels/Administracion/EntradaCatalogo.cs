using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.ViewModels.Administracion;

/// <summary>Un ítem del master de Catálogos: el catálogo, su ícono y el listado que se abre al elegirlo.</summary>
public sealed partial class EntradaCatalogo : ObservableObject
{
    public EntradaCatalogo(ClaveCatalogo clave, string titulo, string icono, CatalogoListadoViewModel listado)
    {
        Clave   = clave;
        Titulo  = titulo;
        Icono   = icono;
        Listado = listado;
    }

    public ClaveCatalogo Clave { get; }
    public string Titulo { get; }
    public string Icono { get; }
    public CatalogoListadoViewModel Listado { get; }

    [ObservableProperty]
    private bool _estaSeleccionada;
}
