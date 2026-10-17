using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Red de seguridad para el bug de UX visto por el usuario en la app real (2026-08-19): varios
/// campos de entrada solo tenían <c>PlaceholderText</c>/<c>Watermark</c> como pista visual, y eso
/// NO es una etiqueta — desaparece apenas el usuario escribe. Mismo enfoque textual que
/// <see cref="ReflexionVistaViewModelTests"/>: lee el .axaml crudo, no monta el árbol visual.
///
/// No es un guardián genérico (a diferencia de <see cref="EspaciadoBotonesEnFilaDeGridTests"/>):
/// cada caso acá fue una decisión de UX puntual (¿va <c:CampoFormulario>, un encabezado de
/// columna, o un TextBlock caption propio?), documentada en el sitio. Por eso son asserts
/// puntuales por campo, no un patrón que recorra todo el árbol de vistas.
/// </summary>
public class EtiquetadoDeCamposTests
{
    private static string LeerAxaml(string rutaRelativaDesdeSrc, [CallerFilePath] string archivoDeEsteTest = "")
    {
        var dirDeTests = Path.GetDirectoryName(archivoDeEsteTest)!;
        var ruta = Path.GetFullPath(Path.Combine(dirDeTests, "..", "..", "src", rutaRelativaDesdeSrc));
        Assert.True(File.Exists(ruta), $"No se encontró el .axaml esperado en: {ruta}");
        return File.ReadAllText(ruta);
    }

    private static bool TieneCampoFormularioCon(string axaml, string etiqueta) =>
        Regex.IsMatch(axaml, $@"<(?:c|v2):CampoFormulario\b[^>]*Etiqueta=""{Regex.Escape(etiqueta)}""");

    private static bool TieneTextBlockCaptionCon(string axaml, string texto, string clase = "caption") =>
        Regex.IsMatch(axaml,
            $@"<TextBlock\b[^>]*(?:Text=""{Regex.Escape(texto)}""[^>]*Classes=""{clase}""" +
            $@"|Classes=""{clase}""[^>]*Text=""{Regex.Escape(texto)}"")[^>]*/?>",
            RegexOptions.Singleline);

    /// <summary>
    /// ControlPoaView: el filtro "Ejercicio" era un NumericUpDown suelto, SIN ninguna etiqueta
    /// (ni siquiera placeholder — NumericUpDown no lo soporta). Se envuelve en CampoFormulario,
    /// el patrón dominante del design system (~120 controles ya lo usan).
    /// </summary>
    [Fact]
    public void ControlPoaView_FiltroEjercicio_TieneEtiquetaCampoFormulario()
    {
        var axaml = LeerAxaml("StockApp.Presentation/Views/Finanzas/ControlPoaView.axaml");
        Assert.True(TieneCampoFormularioCon(axaml, "Ejercicio"),
            "El NumericUpDown de Ejercicio en ControlPoaView debe estar envuelto en "
            + "c:CampoFormulario Etiqueta=\"Ejercicio\".");
    }

    /// <summary>
    /// LineaPoaFormView: la fila repetida de "Asignaciones presupuestales" (ComboBox Fuente +
    /// TextBox Monto) solo tenía placeholder por columna. Como es una FILA que se repite por cada
    /// ítem, una etiqueta por fila sería ruido — se agrega un encabezado de columna una sola vez,
    /// igual que hace un DataGrid con sus Header.
    /// </summary>
    [Fact]
    public void LineaPoaFormView_FilaDeAsignaciones_TieneEncabezadoDeColumnas()
    {
        var axaml = LeerAxaml("StockApp.Presentation/Views/Finanzas/LineaPoaFormView.axaml");
        // v2: el encabezado de columna es el TextBlock "etiqueta-campo" (el "caption" es del tema viejo).
        Assert.True(TieneTextBlockCaptionCon(axaml, "Fuente de financiamiento", "etiqueta-campo"),
            "Falta el encabezado de columna \"Fuente de financiamiento\" sobre la grilla de "
            + "asignaciones de LineaPoaFormView.");
        Assert.True(TieneTextBlockCaptionCon(axaml, "Monto", "etiqueta-campo"),
            "Falta el encabezado de columna \"Monto\" sobre la grilla de asignaciones de "
            + "LineaPoaFormView.");
    }

    /// <summary>
    /// ProductoListView (UI v2): el buscador vive en v2:BarraFiltros, que no tiene etiqueta (patrón de listado
    /// aprobado); lo que explica el campo es la marca de agua, así que no puede faltar.
    /// </summary>
    [Fact]
    public void ProductoListView_Busqueda_EnBarraFiltrosConMarcaDeAgua()
    {
        var axaml = LeerAxaml("StockApp.Presentation/Views/Catalogo/ProductoListView.axaml");
        Assert.Matches(@"<v2:BarraFiltros\b[^>]*\bMarcaAguaBusqueda=""[^""]+""", axaml);
        Assert.Matches(@"<v2:BarraFiltros\b[^>]*\bTextoBusqueda=""\{Binding FiltroBusqueda\}""", axaml);
    }

    /// <summary>
    /// UsuariosAdminView: "Nueva contraseña" (columna derecha, cambio de contraseña del usuario
    /// seleccionado) solo tenía Watermark. Va como TextBlock caption arriba de la fila
    /// [TextBox + botón "Cambiar contraseña"], no CampoFormulario, porque CampoFormulario apila
    /// verticalmente (label arriba del Content) y desalinearía el botón de al lado.
    ///
    /// El panel "NUEVO USUARIO" del mismo archivo (Nombre de usuario/Nombre completo/Contraseña/
    /// Rol) tenía el mismo problema, de alcance más grande (4 campos, no 1) — quedó fuera de esta
    /// tanda a propósito y se corrigió en la tanda siguiente (ver los 4 tests de abajo).
    /// </summary>
    [Fact]
    public void UsuariosAdminView_NuevaContrasena_TieneEtiquetaCaption()
    {
        var axaml = LeerAxaml("StockApp.Presentation/Views/Administracion/UsuariosAdminView.axaml");
        // v2: la etiqueta la da el v2:CampoFormulario (el caption es del tema viejo); el botón va al lado, abajo.
        Assert.True(TieneCampoFormularioCon(axaml, "Nueva contraseña"),
            "Falta un v2:CampoFormulario Etiqueta=\"Nueva contraseña\" alrededor del "
            + "TextBox de NuevaContrasenaParaSeleccionado en UsuariosAdminView.");
    }

    /// <summary>
    /// UsuariosAdminView, panel "NUEVO USUARIO": los 4 campos (Nombre de usuario, Nombre
    /// completo, Contraseña, Rol -- este último ni siquiera tenía Watermark, estaba
    /// completamente mudo) solo tenían Watermark como pista visual. Están apilados
    /// verticalmente en un StackPanel sin ningún control al costado que se desalinee, así que
    /// corresponde el mismo criterio que ControlPoaView/ProductoListView: c:CampoFormulario.
    /// </summary>
    [Fact]
    public void UsuariosAdminView_PanelNuevoUsuario_TieneEtiquetasCampoFormulario()
    {
        var axaml = LeerAxaml("StockApp.Presentation/Views/Administracion/UsuariosAdminView.axaml");

        Assert.True(TieneCampoFormularioCon(axaml, "Nombre de usuario"),
            "El TextBox de NuevoNombreUsuario en UsuariosAdminView debe estar envuelto en "
            + "c:CampoFormulario Etiqueta=\"Nombre de usuario\".");
        Assert.True(TieneCampoFormularioCon(axaml, "Nombre completo (opcional)"),
            "El TextBox de NuevoNombreCompleto en UsuariosAdminView debe estar envuelto en "
            + "c:CampoFormulario Etiqueta=\"Nombre completo (opcional)\".");
        Assert.True(TieneCampoFormularioCon(axaml, "Contraseña"),
            "El TextBox de NuevaContrasenaPlan en UsuariosAdminView debe estar envuelto en "
            + "c:CampoFormulario Etiqueta=\"Contraseña\".");
        Assert.True(TieneCampoFormularioCon(axaml, "Rol"),
            "El ComboBox de NuevoRol en UsuariosAdminView debe estar envuelto en "
            + "c:CampoFormulario Etiqueta=\"Rol\" (antes no tenía ni siquiera Watermark).");
    }
}
