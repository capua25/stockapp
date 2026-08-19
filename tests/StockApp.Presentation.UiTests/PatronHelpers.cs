using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Helpers compartidos por <see cref="GuardianDePatronTests"/> y por las tandas 6-12, que amplian
/// la misma tabla de vistas. 42 de las 55 vistas de la Fase B no tienen ni un test de UI propio;
/// en vez de escribir uno por vista, el guardian monta N vistas de golpe y mide, sobre cada una,
/// los tres invariantes de patron que el compilador de bindings (AvaloniaUseCompiledBindingsByDefault)
/// NO ve: existe un HeaderVista, el margen exterior es el estandar, y no quedo ningun Opacity
/// literal decorativa.
///
/// Decision de montaje: <see cref="Montar"/> instancia la vista SIN asignar un DataContext real
/// (ningun ViewModel, ningun fake). Es deliberado: los tres invariantes son estructurales, no de
/// datos, y montar sin VM evita construir un ViewModel + fakes por cada una de las 55 vistas solo
/// para un guardian de bloque. Ver el comentario de <see cref="OpacidadesLiteralesDe"/> para el
/// efecto colateral que esto tiene sobre esa medicion en particular.
/// </summary>
public static class PatronHelpers
{
    /// <summary>
    /// Instancia el tipo de vista via <see cref="Activator.CreateInstance(Type)"/> (mismo
    /// mecanismo que <c>ViewLocator.Build</c> usa en produccion), la monta como Content de una
    /// Window headless y corre el layout. Sin DataContext: ver el comentario de la clase.
    /// </summary>
    public static Control Montar(Type tipoVista)
    {
        var vista = (Control)Activator.CreateInstance(tipoVista)!;
        var window = new Window { Width = 1200, Height = 900, Content = vista };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return vista;
    }

    /// <summary>
    /// Localiza el HeaderVista de una vista montada. Devuelve null si no hay ninguno.
    /// HeaderVista es TemplatedControl (no UserControl): vive en el mismo arbol visual que la
    /// vista que lo contiene, asi que GetVisualDescendants lo alcanza sin problema (T7 del plan:
    /// el NameScope propio de un UserControl solo afecta la busqueda por x:Name, no por tipo).
    /// </summary>
    public static HeaderVista? HeaderDe(Control vista)
        => vista.GetVisualDescendants().OfType<HeaderVista>().FirstOrDefault();

    /// <summary>
    /// Margin del panel raiz "real" de la vista: el primer descendiente Layoutable, en orden de
    /// arbol (preorder — un padre se visita antes que sus hijos, confirmado contra
    /// VisualExtensions.GetVisualDescendants), que tiene un Margin distinto de default.
    ///
    /// Por que "primero con Margin no-default" y no "primer hijo directo del Content": en P1/P3
    /// simples (ProductoListView, ProductoFormView, MovimientoFormControl) el panel margenado
    /// ES el primer hijo directo. Pero en P0 (InicioView) y el P1 compuesto
    /// (IngresoPorFacturaView) el Content de primer nivel es un Grid/ScrollViewer sin margen
    /// propio, envolviendo mas adentro al StackPanel/DockPanel que sí lo tiene. Buscar "primer
    /// Layoutable con Margin != default" en vez de "primer hijo directo" alcanza el contenedor
    /// correcto en los dos casos sin necesitar una rama de codigo por patron, porque el
    /// contenedor estandar de margen SIEMPRE antecede en el arbol a cualquier control interno
    /// que pudiera tener un Margin propio incidental (un boton con Margin="0,0,8,0", etc.).
    /// </summary>
    public static Thickness? MargenExteriorDe(Control vista)
    {
        foreach (var layoutable in vista.GetVisualDescendants().OfType<Layoutable>())
        {
            if (layoutable.Margin != default)
                return layoutable.Margin;
        }
        return null;
    }

    /// <summary>
    /// Controles con Opacity literal decorativa distinta de 1.0, realmente presentes en el
    /// arbol visual montado.
    ///
    /// GAP DE PLAN CORREGIDO (verificado empiricamente, ver ledger de la Task 6.0): la premisa
    /// del plan — "GetDiagnostic(OpacityProperty).Priority == BindingPriority.LocalValue
    /// distingue la Opacity literal de la que viene por converter" — es PARCIALMENTE falsa. Un
    /// Opacity="0.5" literal y un Opacity="{Binding X, Converter=...}" YA RESUELTO contra un
    /// DataContext real reportan la MISMA prioridad, LocalValue: Avalonia asigna esa prioridad
    /// por defecto tanto a un SetValue directo como a un Bind(). Esa mitad de la premisa no
    /// sirve para lo que el plan queria.
    ///
    /// Pero la otra mitad SI hace falta, por una razon que el plan no menciona: FluentTheme le
    /// pone Opacity a partes internas de sus propios controles (el fade de los RepeatButton/
    /// Thumb/Panel de un ScrollBar, el glifo de orden de un DataGridColumnHeader) via Setter de
    /// ControlTheme — Priority=Style/Template, NUNCA LocalValue. Sin filtrar por Priority, el
    /// guardian confundia chrome del tema (Rectangle/RepeatButton/Panel de scrollbars, hasta en
    /// ProductoListView con 0 filas) con Opacity de autor. Filtrar por
    /// Priority==BindingPriority.LocalValue elimina ese ruido.
    ///
    /// La otra mitad del problema (literal vs. converter, ambos LocalValue) se resuelve por
    /// COMO <see cref="Montar"/> monta la vista, no por esta API: sin ViewModel real, el
    /// ItemsSource de cualquier DataGrid queda vacio y las DataGridTemplateColumn.CellTemplate
    /// — donde vive el Opacity con ActivoOpacidadConverter en las 14 vistas P1 — nunca se
    /// realizan como visual (no hay filas que templatizar). Si una tanda futura montara una
    /// vista CON ViewModel real, este helper dejaria de ser preciso para esa vista puntual.
    /// </summary>
    public static IReadOnlyList<Control> OpacidadesLiteralesDe(Control vista)
        => vista.GetVisualDescendants().OfType<Control>()
            .Where(c => Math.Abs(c.Opacity - 1.0) > 0.0001)
            .Where(c => c.GetDiagnostic(Visual.OpacityProperty).Priority == BindingPriority.LocalValue)
            .ToList();
}
