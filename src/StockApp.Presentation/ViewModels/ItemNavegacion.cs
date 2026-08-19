using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Un item del menu lateral. Reemplaza los 26 bloques de XAML copiados literalmente en
/// ShellMainView.axaml.
///
/// No es un record inmutable puro: <see cref="EstaActivo"/> tiene que poder cambiar cada vez que
/// el usuario navega a otra sección, sin reconstruir la lista de grupos (eso rompería la
/// identidad de instancia que el ItemsControl del sidebar usa para trackear qué está expandido).
/// Ruling 2026-08-19: Avalonia no acepta <c>{Binding}</c> dentro de <c>ConverterParameter</c>, así
/// que el XAML de la Task 5.3 ya no puede comparar <c>SeccionActiva</c> contra el item con un
/// converter, como hacía cada botón hasta ahora (<c>Classes.active</c> +
/// <c>ObjectConverters.Equal</c>, <c>ConverterParameter=Productos</c>). En vez de eso,
/// <see cref="ShellMainViewModel"/> calcula <see cref="EstaActivo"/> una vez por navegación (en
/// el setter parcial de <c>SeccionActiva</c>) y el XAML solo lo bindea.
/// </summary>
/// <param name="Titulo">Texto visible. Se conserva EXACTO respecto del XAML actual.</param>
/// <param name="Icono">Valor para i:Icon, con prefijo mdi (ej. "mdi-package-variant").</param>
/// <param name="Comando">El RelayCommand de navegacion del ShellMainViewModel.</param>
/// <param name="Seccion">Clave que se compara contra SeccionActiva para marcar el item activo.</param>
/// <param name="EsVisible">Resultado del gate de permiso, evaluado al construir el menu.</param>
public partial class ItemNavegacion : ObservableObject
{
    public ItemNavegacion(string titulo, string icono, ICommand comando, string seccion, bool esVisible)
    {
        Titulo = titulo;
        Icono = icono;
        Comando = comando;
        Seccion = seccion;
        EsVisible = esVisible;
    }

    public string Titulo { get; }

    public string Icono { get; }

    public ICommand Comando { get; }

    public string Seccion { get; }

    public bool EsVisible { get; }

    [ObservableProperty]
    private bool _estaActivo;
}
