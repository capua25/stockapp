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
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, UmbralAprobado? aprobado = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar);

    [AvaloniaFact] public void UsuariosSinSeleccion_IgualAlMockup() => Comparar(DatosUsuarios.SinSeleccion(), "administracion/usuarios-sin-seleccion.html");
    [AvaloniaFact] public void UsuariosSinSeleccion_IgualAlMockup_1366x768() => Comparar(DatosUsuarios.SinSeleccion(), "administracion/usuarios-sin-seleccion.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void Usuarios_IgualAlMockup() => Comparar(DatosUsuarios.OperadorSeleccionado(), "administracion/usuarios.html");
    [AvaloniaFact] public void Usuarios_IgualAlMockup_1366x768() => Comparar(DatosUsuarios.OperadorSeleccionado(), "administracion/usuarios.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void UsuariosAdmin_IgualAlMockup() => Comparar(DatosUsuarios.AdminSeleccionado(), "administracion/usuarios-admin.html");
    [AvaloniaFact] public void UsuariosAdmin_IgualAlMockup_1366x768() => Comparar(DatosUsuarios.AdminSeleccionado(), "administracion/usuarios-admin.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void UsuariosInactivo_IgualAlMockup() => Comparar(DatosUsuarios.InactivoSeleccionado(), "administracion/usuarios-inactivo.html");
    [AvaloniaFact] public void UsuariosInactivo_IgualAlMockup_1366x768() => Comparar(DatosUsuarios.InactivoSeleccionado(), "administracion/usuarios-inactivo.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void UsuariosAltaError_IgualAlMockup() => Comparar(DatosUsuarios.AltaConError(), "administracion/usuarios-alta-error.html");
    [AvaloniaFact] public void UsuariosAltaError_IgualAlMockup_1366x768() => Comparar(DatosUsuarios.AltaConError(), "administracion/usuarios-alta-error.html", TamanoLienzo.Municipio);
}
