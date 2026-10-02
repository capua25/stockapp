using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.VisualTests.Catalogo;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Validación end-to-end de la Fase 0 (spec §7): el catálogo con TODOS los componentes v2 tiene
/// que quedar igual a su mockup. Es también la pantalla con la que se calibra el umbral.
/// </summary>
[Trait("Categoria", "ComparacionVisual")]
public class CatalogoComponentesTests
{
    [AvaloniaFact]
    public void Catalogo_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new CatalogoComponentesView(), new CatalogoComponentesViewModel(), "componentes/catalogo.html");

    /// <summary>
    /// Task 5b, Step 4: calibración de cordura a 1366x768 con el MISMO umbral (spec §6.2). Si no
    /// pasa, se diagnostica; nunca se sube el umbral.
    /// </summary>
    [AvaloniaFact]
    public void Catalogo_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new CatalogoComponentesView(), new CatalogoComponentesViewModel(), "componentes/catalogo.html",
            tamano: TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void Catalogo_MuestraCadaComponenteYControlV2()
    {
        // Si un componente falta en el catálogo, la calibración no lo cubre.
        var vista = new CatalogoComponentesView();
        RenderizadorVista.RenderizarAPng(vista, new CatalogoComponentesViewModel(),
            Path.Combine(Directory.CreateTempSubdirectory("catalogo-").FullName, "catalogo.png"));
        var tipos = vista.GetVisualDescendants().Select(v => v.GetType()).ToHashSet();

        Assert.Contains(typeof(EncabezadoPagina), tipos);
        Assert.Contains(typeof(TarjetaContenido), tipos);
        Assert.Contains(typeof(BarraFiltros), tipos);
        Assert.Contains(typeof(DataGrid), tipos);
        Assert.Contains(typeof(CampoFormulario), tipos);
        Assert.Contains(typeof(Insignia), tipos);
        Assert.Contains(typeof(Alerta), tipos);
        Assert.Contains(typeof(TarjetaMetrica), tipos);
        Assert.Contains(typeof(TarjetaAcceso), tipos);
        Assert.Contains(typeof(EstadoVacio), tipos);
        Assert.Contains(typeof(ComboBox), tipos);
        Assert.Contains(typeof(CalendarDatePicker), tipos);
        Assert.Contains(typeof(NumericUpDown), tipos);
        Assert.Contains(typeof(CheckBox), tipos);
        var clasesDeBoton = vista.GetVisualDescendants().OfType<Button>().SelectMany(b => b.Classes).ToHashSet();
        Assert.Superset(new HashSet<string> { "primario", "secundario", "ghost", "peligro" }, clasesDeBoton);
    }
}
