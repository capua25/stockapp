using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Administracion;

/// <summary>Usuarios, dentro del shell real con el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class UsuariosComparacionTests
{

    private static UmbralAprobado Aprobacion(int teselas, string motivo, string? mutacionPalida = null) => new(
        teselas, "Martin", "2026-10-17", motivo, mutacionPalida);

    private const string MutacionUsuarios1280 =
        "Un Border de 260 con V2FondoBrush (relleno pálido equivocado) bajo el estado vacío del detalle de Usuarios: 583 teselas a 1280 (sin selección y alta con error), contra 23 aprobadas";

    private const string MutacionUsuarios1366 =
        "Un Border de 260 con V2FondoBrush (relleno pálido equivocado) bajo el estado vacío del detalle de Usuarios: 662 teselas a 1366 (sin selección y alta con error), contra 22 aprobadas";

    private const string MotivoTexto =
        "Antialias Skia vs Chromium en el texto (negritas de títulos y centrado de los estados a medio píxel); estructura de bordes y fondos " +
        "idéntica (cobertura 1,000 con 1 px de tolerancia), tinta sin corrimiento y teselas solo sobre glifos. Tope = medido.";

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, UmbralAprobado? aprobado = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar);

    [AvaloniaFact] public void UsuariosSinSeleccion_IgualAlMockup() => Comparar(DatosUsuarios.SinSeleccion(), "administracion/usuarios-sin-seleccion.html", aprobado: Aprobacion(23, MotivoTexto, MutacionUsuarios1280));
    [AvaloniaFact] public void UsuariosSinSeleccion_IgualAlMockup_1366x768() => Comparar(DatosUsuarios.SinSeleccion(), "administracion/usuarios-sin-seleccion.html", TamanoLienzo.Municipio, aprobado: Aprobacion(22, MotivoTexto, MutacionUsuarios1366));
    [AvaloniaFact] public void Usuarios_IgualAlMockup() => Comparar(DatosUsuarios.OperadorSeleccionado(), "administracion/usuarios.html");
    [AvaloniaFact] public void Usuarios_IgualAlMockup_1366x768() => Comparar(DatosUsuarios.OperadorSeleccionado(), "administracion/usuarios.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void UsuariosAdmin_IgualAlMockup() => Comparar(DatosUsuarios.AdminSeleccionado(), "administracion/usuarios-admin.html");
    [AvaloniaFact] public void UsuariosAdmin_IgualAlMockup_1366x768() => Comparar(DatosUsuarios.AdminSeleccionado(), "administracion/usuarios-admin.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void UsuariosInactivo_IgualAlMockup() => Comparar(DatosUsuarios.InactivoSeleccionado(), "administracion/usuarios-inactivo.html", aprobado: Aprobacion(15, MotivoTexto));
    [AvaloniaFact] public void UsuariosInactivo_IgualAlMockup_1366x768() => Comparar(DatosUsuarios.InactivoSeleccionado(), "administracion/usuarios-inactivo.html", TamanoLienzo.Municipio, aprobado: Aprobacion(15, MotivoTexto));
    [AvaloniaFact] public void UsuariosAltaError_IgualAlMockup() => Comparar(DatosUsuarios.AltaConError(), "administracion/usuarios-alta-error.html", aprobado: Aprobacion(23, MotivoTexto, MutacionUsuarios1280));
    [AvaloniaFact] public void UsuariosAltaError_IgualAlMockup_1366x768() => Comparar(DatosUsuarios.AltaConError(), "administracion/usuarios-alta-error.html", TamanoLienzo.Municipio, aprobado: Aprobacion(22, MotivoTexto, MutacionUsuarios1366));
}
