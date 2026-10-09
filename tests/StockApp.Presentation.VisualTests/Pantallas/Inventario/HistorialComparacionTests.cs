using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Historial de movimientos (spec §5.1) dentro del shell real, con la vista resuelta por ViewLocator. El sidebar
/// tiene su propia comparación (ShellComparacionTests): se oculta en los dos renders sin mover el layout.
/// </summary>
[Trait("Categoria", "ComparacionVisual")]
public class HistorialComparacionTests
{
    private static readonly string[] OcultarEnMockup = [".v2-sidebar"];

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null,
        IReadOnlyCollection<string>? recortesPermitidos = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: OcultarEnMockup, prepararApp: ProductosComparacionTests.OcultarSidebar,
            recortesPermitidos: recortesPermitidos);

    // Cortes a propósito, con elipsis y tooltip, que el mockup también corta: PRODUCTO y COMENTARIO quedan en su piso
    // de 160 porque las diez columnas no entran.
    private static readonly string[] CortesDelMockup =
    [
        "Cemento portland 50 kg", "Pintura látex blanca 20 L", "Hipoclorito de sodio 5 L", "Bolsas de residuo 80x110",
        "Remito 0042-00001234",
    ];

    [AvaloniaFact]
    public void Historial_IgualAlMockup() =>
        Comparar(DatosHistorial.ConMovimientos(), "inventario/historial.html", recortesPermitidos: CortesDelMockup);

    [AvaloniaFact]
    public void Historial_IgualAlMockup_1366x768() =>
        Comparar(DatosHistorial.ConMovimientos(), "inventario/historial.html", TamanoLienzo.Municipio, CortesDelMockup);

    [AvaloniaFact]
    public void HistorialCargando_IgualAlMockup() => Comparar(DatosHistorial.Cargando(), "inventario/historial-cargando.html");

    [AvaloniaFact]
    public void HistorialCargando_IgualAlMockup_1366x768() =>
        Comparar(DatosHistorial.Cargando(), "inventario/historial-cargando.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void HistorialSinPermiso_IgualAlMockup() => Comparar(DatosHistorial.SinPermiso(), "inventario/historial-sin-permiso.html");

    [AvaloniaFact]
    public void HistorialSinPermiso_IgualAlMockup_1366x768() =>
        Comparar(DatosHistorial.SinPermiso(), "inventario/historial-sin-permiso.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void HistorialVacio_IgualAlMockup() => Comparar(DatosHistorial.SinMovimientos(), "inventario/historial-vacio.html");

    [AvaloniaFact]
    public void HistorialVacio_IgualAlMockup_1366x768() =>
        Comparar(DatosHistorial.SinMovimientos(), "inventario/historial-vacio.html", TamanoLienzo.Municipio);

    // Además de los cortes de siempre, el producto largo de la fila 3 y su usuario, que pasa el tope de USUARIO.
    private static readonly string[] CortesDeLaCeldaCortada = [.. CortesDelMockup, DatosHistorial.ProductoLargo, DatosHistorial.UsuarioLargo];

    [AvaloniaFact]
    public void HistorialCeldaCortada_IgualAlMockup() =>
        Comparar(DatosHistorial.ConCeldaCortada(), "inventario/historial-celda-cortada.html", recortesPermitidos: CortesDeLaCeldaCortada);

    [AvaloniaFact]
    public void HistorialCeldaCortada_IgualAlMockup_1366x768() =>
        Comparar(DatosHistorial.ConCeldaCortada(), "inventario/historial-celda-cortada.html", TamanoLienzo.Municipio, CortesDeLaCeldaCortada);
}
