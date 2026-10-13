using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Formulario de ingreso dentro del shell real, con el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class IngresoFormComparacionTests
{
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar);

    [AvaloniaFact] public void IngresoNuevo_IgualAlMockup() => Comparar(DatosIngresos.Nuevo(), "finanzas/ingreso-nuevo.html");
    [AvaloniaFact] public void IngresoNuevo_IgualAlMockup_1366x768() => Comparar(DatosIngresos.Nuevo(), "finanzas/ingreso-nuevo.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresoEditar_IgualAlMockup() => Comparar(DatosIngresos.Editar(), "finanzas/ingreso-editar.html");
    [AvaloniaFact] public void IngresoEditar_IgualAlMockup_1366x768() => Comparar(DatosIngresos.Editar(), "finanzas/ingreso-editar.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresoErrorCampo_IgualAlMockup() => Comparar(DatosIngresos.ConErrorDeCampo(), "finanzas/ingreso-error-campo.html");
    [AvaloniaFact] public void IngresoErrorCampo_IgualAlMockup_1366x768() => Comparar(DatosIngresos.ConErrorDeCampo(), "finanzas/ingreso-error-campo.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresoErrorServidor_IgualAlMockup() => Comparar(DatosIngresos.ConErrorDelServidor(), "finanzas/ingreso-error-servidor.html");
    [AvaloniaFact] public void IngresoErrorServidor_IgualAlMockup_1366x768() => Comparar(DatosIngresos.ConErrorDelServidor(), "finanzas/ingreso-error-servidor.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresoSinPermiso_IgualAlMockup() => Comparar(DatosIngresos.SinPermisoAlInicializar(), "finanzas/ingreso-sin-permiso.html");
    [AvaloniaFact] public void IngresoSinPermiso_IgualAlMockup_1366x768() => Comparar(DatosIngresos.SinPermisoAlInicializar(), "finanzas/ingreso-sin-permiso.html", TamanoLienzo.Municipio);
}
