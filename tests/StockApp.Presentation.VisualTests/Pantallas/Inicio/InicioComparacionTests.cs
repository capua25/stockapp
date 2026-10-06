using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Inicio;

[Trait("Categoria", "ComparacionVisual")]
public class InicioComparacionTests
{
    // Pantalla de contenido (convención 6): shell real, Inicio resuelto por ViewLocator. El sidebar ya tiene su
    // comparación (ShellComparacionTests) y la marca de agua no se compara (sin test propio por decisión del
    // usuario, U9): se ocultan en AMBOS renders sin mover el layout. Admin, OperadorMixto y AdminSinDatos
    // superan las 11 teselas calibradas solo por tono de texto (spec §6.5): umbral aprobado por el usuario,
    // con la mutación pálida equivalente a M2 corrida en cada pantalla y registrada. OperadorSinFinanzasNiTareas
    // pasa con lo calibrado. Límite conocido: en OperadorMixto una sola Insignia "Vencida" sin Variante=Peligro
    // no cambia las teselas (20 con y sin el defecto), tampoco con 11.
    private static readonly string[] OcultarEnMockup = [".v2-sidebar", ".marca-agua"];

    public static void OcultarSidebarYMarcaDeAgua(Control vista)
    {
        var shell = (ShellMainView)vista;
        shell.FindControl<Border>("Sidebar")!.IsVisible = false;
        var inicio = shell.GetVisualDescendants().OfType<InicioView>().Single();
        inicio.FindControl<Image>("MarcaAgua")!.IsVisible = false;
    }

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, UmbralAprobado? aprobado = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: OcultarEnMockup, prepararApp: OcultarSidebarYMarcaDeAgua);

    private const string Tono = "solo tono de texto oscuro (títulos de página, sección, fila de tarea y acceso), geometría igual";

    private static readonly UmbralAprobado AdminEstandar = new(40, "Martin", "2026-10-06",
        "Inicio Administrador 1280x760: 40 teselas, " + Tono,
        MutacionPalidaEnRojo: "Insignia Vencida sin Variante=Peligro: 43 teselas; Insignia Próxima sin Variante=Advertencia: 51");

    private static readonly UmbralAprobado AdminMunicipio = new(46, "Martin", "2026-10-06",
        "Inicio Administrador 1366x768: 46 teselas, " + Tono,
        MutacionPalidaEnRojo: "Insignia Vencida sin Variante=Peligro: 49 teselas; Insignia Próxima sin Variante=Advertencia: 57");

    private static readonly UmbralAprobado OperadorMixtoEstandar = new(20, "Martin", "2026-10-06",
        "Inicio operador mixto 1280x760: 20 teselas, " + Tono,
        MutacionPalidaEnRojo: "Insignia Próxima sin Variante=Advertencia: 27 teselas; AlertaFacturasVencidas sin Variante=Advertencia: 205. "
            + "Insignia Vencida sin Variante=Peligro: 20, no se detecta (límite de la métrica, informado al usuario)");

    private static readonly UmbralAprobado OperadorMixtoMunicipio = new(20, "Martin", "2026-10-06",
        "Inicio operador mixto 1366x768: 20 teselas, " + Tono,
        MutacionPalidaEnRojo: "Insignia Próxima sin Variante=Advertencia: 27 teselas; AlertaFacturasVencidas sin Variante=Advertencia: 222. "
            + "Insignia Vencida sin Variante=Peligro: 20, no se detecta (límite de la métrica, informado al usuario)");

    private static readonly UmbralAprobado AdminSinDatosEstandar = new(28, "Martin", "2026-10-06",
        "Inicio Administrador sin datos 1280x760: 28 teselas, " + Tono,
        MutacionPalidaEnRojo: "AlertaBackupDesconocido sin Variante=Advertencia (pasa a Info): 213 teselas");

    private static readonly UmbralAprobado AdminSinDatosMunicipio = new(32, "Martin", "2026-10-06",
        "Inicio Administrador sin datos 1366x768: 32 teselas, " + Tono,
        MutacionPalidaEnRojo: "AlertaBackupDesconocido sin Variante=Advertencia (pasa a Info): 234 teselas");

    [AvaloniaFact]
    public void Admin_IgualAlMockup() => Comparar(DatosInicio.Admin(), "inicio/inicio-admin.html", aprobado: AdminEstandar);

    [AvaloniaFact]
    public void Admin_IgualAlMockup_1366x768() =>
        Comparar(DatosInicio.Admin(), "inicio/inicio-admin.html", TamanoLienzo.Municipio, AdminMunicipio);

    [AvaloniaFact]
    public void OperadorMixto_IgualAlMockup() => Comparar(DatosInicio.OperadorMixto(), "inicio/inicio-operador.html", aprobado: OperadorMixtoEstandar);

    [AvaloniaFact]
    public void OperadorMixto_IgualAlMockup_1366x768() =>
        Comparar(DatosInicio.OperadorMixto(), "inicio/inicio-operador.html", TamanoLienzo.Municipio, OperadorMixtoMunicipio);

    [AvaloniaFact]
    public void OperadorSinFinanzasNiTareas_IgualAlMockup() =>
        Comparar(DatosInicio.OperadorSinFinanzasNiTareas(), "inicio/inicio-operador-sin-finanzas.html");

    [AvaloniaFact]
    public void OperadorSinFinanzasNiTareas_IgualAlMockup_1366x768() =>
        Comparar(DatosInicio.OperadorSinFinanzasNiTareas(), "inicio/inicio-operador-sin-finanzas.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void AdminSinDatos_IgualAlMockup() => Comparar(DatosInicio.AdminSinDatos(), "inicio/inicio-admin-sin-datos.html", aprobado: AdminSinDatosEstandar);

    [AvaloniaFact]
    public void AdminSinDatos_IgualAlMockup_1366x768() =>
        Comparar(DatosInicio.AdminSinDatos(), "inicio/inicio-admin-sin-datos.html", TamanoLienzo.Municipio, AdminSinDatosMunicipio);
}
