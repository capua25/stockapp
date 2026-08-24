using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardián DINÁMICO de repositorio (Parte 3 del bugfix "botón sin contenido visible en celda de
/// DataGrid" -- ver comentario XAML en <c>DataGridCell.sin-padding-vertical</c> de
/// IngresoPorFacturaView.axaml y NuevaImportacionView.axaml, y el guardián puntual de render en
/// IngresoPorFacturaGrillaRenglonesTests.BotonQuitar_TextoInterno_TieneAltoRenderizadoVisible_...).
///
/// LA ARITMÉTICA (para quien lea esto sin contexto, en seis meses):
/// Themes/DataGrid.axaml fija <c>RowHeight="36"</c> para toda fila de todo DataGrid de la app, y
/// <c>DataGridCell.Padding = PaddingCelda = "12,8"</c> (Themes/Tokens.axaml) -- 8px arriba + 8px
/// abajo = 16px de padding vertical, dejando sólo 36 - 16 = 20px útiles DENTRO de la celda.
/// Un <c>Button</c> (Controls.axaml: Padding "16,8" = 16px vertical + BorderThickness 1px arriba
/// y abajo = 2px) necesita ~36px para que su contenido no quede aplastado -- no entra en los 20px
/// disponibles, y el contenido (texto o ícono) se renderiza con 2px de alto: visualmente
/// invisible aunque el control existe, tiene foreground correcto y no está oculto. Un ComboBox o
/// TextBox con su padding propio tienen el mismo problema por la misma cuenta. Bajar el padding
/// del CONTROL no alcanza (ni la variante ".compacto" del UI Kit, que da ~25px, entra en 20px).
///
/// EL FIX: sacarle el padding VERTICAL a la CELDA (no al control) para la columna afectada, vía
/// <c>CellStyleClasses="sin-padding-vertical"</c> en la <c>DataGridTemplateColumn</c> más
/// <c>&lt;Style Selector="DataGridCell.sin-padding-vertical"&gt;&lt;Setter Property="Padding"
/// Value="12,0" /&gt;&lt;/Style&gt;</c> en los <c>UserControl.Styles</c> de la vista. Con la
/// celda en 36px enteros, el control resuelve su propio padding adentro sin quedar aplastado
/// (medido: el TextBlock del botón "Quitar" pasa de 2px a 18px de alto real).
///
/// QUÉ HACE ESTE GUARDIÁN: recorre TODOS los .axaml de src/StockApp.Presentation/Views/ (barrido
/// dinámico -- Directory.EnumerateFiles, ningún archivo hardcodeado) buscando toda
/// <c>DataGridTemplateColumn</c> cuyo <c>DataGridTemplateColumn.CellTemplate</c> (SÓLO el modo de
/// sólo-lectura -- ver exclusión de CellEditingTemplate más abajo) contenga un <c>Button</c>,
/// <c>ComboBox</c> o <c>TextBox</c> (los tres controles del UI Kit con padding vertical propio
/// capaz de superar los 20px útiles). Si la columna no declara
/// <c>CellStyleClasses="sin-padding-vertical"</c>, es un candidato nuevo a la misma trampa que
/// ya pasó tres veces en este repo (Quitar, y los dos ghost+mdi-pencil de NuevaImportacionView) y
/// el guardián falla, con un mensaje que explica la aritmética y el fix exacto a aplicar.
///
/// EXCLUSIÓN DELIBERADA DE CellEditingTemplate: NuevaImportacionView.axaml tiene 11 controles
/// (6 ComboBox + 5 TextBox) dentro de <c>DataGridTemplateColumn.CellEditingTemplate</c> con la
/// misma aritmética sospechosa (esa pantalla está "en stand by" -- decisión del usuario de no
/// tocarla en este bugfix). No son un caso a exceptuar uno por uno: este guardián directamente no
/// mira CellEditingTemplate, sólo CellTemplate (el modo de sólo-lectura, el único que realmente
/// se ve sin que el usuario entre en modo edición de la celda). Es deuda conocida, documentada
/// acá y en el reporte de la tarea -- no una laguna accidental del guardián.
///
/// Parseo por XML real (XDocument), no regex línea a línea (a diferencia de
/// RelojDeParedEnBuclesDeEsperaGuardTests): acá SÍ hace falta entender anidamiento de elementos
/// (CellTemplate vs CellEditingTemplate, y qué controles caen DENTRO de cuál), que un regex por
/// línea no puede resolver con seguridad.
/// </summary>
public class DataGridCeldaConControlPaddingPropioGuardTests
{
    private const string ClaseDeFix = "sin-padding-vertical";

    private static readonly string[] ControlesConPaddingPropioSospechoso = { "Button", "ComboBox", "TextBox" };

    [Fact]
    public void NingunaColumnaDeDataGrid_TieneControlConPaddingPropio_SinLaClaseDeFixEnLaCelda()
    {
        var raiz = ResolverRaizDelRepo();
        var directorioViews = Path.Combine(raiz, "src", "StockApp.Presentation", "Views");

        Assert.True(Directory.Exists(directorioViews),
            $"No se encontró {directorioViews} -- el guardián probablemente está mal apuntado.");

        var archivos = Directory.EnumerateFiles(directorioViews, "*.axaml", SearchOption.AllDirectories)
            .Where(EsArchivoFuenteReal)
            .ToList();

        // Guarda contra un descubrimiento roto que "pasaría" por no encontrar nada que revisar.
        Assert.True(archivos.Count > 10,
            $"Sólo se encontraron {archivos.Count} archivos .axaml bajo Views/ -- el descubrimiento " +
            "dinámico probablemente está mal apuntado (revisar ResolverRaizDelRepo/directorioViews).");

        var ofensores = new List<string>();
        foreach (var archivo in archivos)
            ofensores.AddRange(ColumnasSinElFixEnEsteArchivo(archivo, raiz));

        Assert.True(ofensores.Count == 0,
            "Columna(s) de DataGrid con un control de padding propio (Button/ComboBox/TextBox) " +
            "dentro de CellTemplate, sin CellStyleClasses=\"sin-padding-vertical\" en la " +
            "DataGridTemplateColumn: el contenido puede renderizarse aplastado dentro de la celda " +
            "(RowHeight 36 - PaddingCelda vertical 16 = sólo 20px útiles; el control necesita más). " +
            "Agregá CellStyleClasses=\"sin-padding-vertical\" a la columna y, si la vista no lo " +
            "tiene ya, un <Style Selector=\"DataGridCell.sin-padding-vertical\"><Setter " +
            "Property=\"Padding\" Value=\"12,0\" /></Style> en su UserControl.Styles (ver " +
            "IngresoPorFacturaView.axaml o NuevaImportacionView.axaml para el patrón completo). " +
            "Ofensores: " + string.Join("; ", ofensores));
    }

    /// <summary>obj/ y bin/ pueden traer copias generadas que no son fuente versionada -- mismo
    /// criterio que RelojDeParedEnBuclesDeEsperaGuardTests.EsArchivoFuenteReal.</summary>
    private static bool EsArchivoFuenteReal(string ruta)
    {
        var segmentos = ruta.Split(Path.DirectorySeparatorChar);
        return !segmentos.Contains("obj") && !segmentos.Contains("bin");
    }

    private static IEnumerable<string> ColumnasSinElFixEnEsteArchivo(string ruta, string raiz)
    {
        XDocument documento;
        try
        {
            documento = XDocument.Load(ruta, LoadOptions.SetLineInfo);
        }
        catch (System.Xml.XmlException ex)
        {
            // Un .axaml que no parsea como XML es un problema del propio archivo (o del guardián
            // apuntando a algo que no debería), no algo que este test deba silenciar: se reporta
            // como ofensor explícito en vez de tragarse la excepción.
            return new[] { $"{Path.GetRelativePath(raiz, ruta)}: no se pudo parsear como XML ({ex.Message})" };
        }

        var rutaRelativa = Path.GetRelativePath(raiz, ruta);
        var ofensores = new List<string>();

        foreach (var columna in documento.Descendants().Where(e => e.Name.LocalName == "DataGridTemplateColumn"))
        {
            var cellTemplate = columna.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "DataGridTemplateColumn.CellTemplate");
            if (cellTemplate is null)
                continue; // Sólo CellEditingTemplate (o ninguno) -- fuera del alcance de este guardián.

            var controlSospechoso = cellTemplate.Descendants()
                .FirstOrDefault(e => ControlesConPaddingPropioSospechoso.Contains(e.Name.LocalName));
            if (controlSospechoso is null)
                continue;

            var clases = (columna.Attribute("CellStyleClasses")?.Value ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (clases.Contains(ClaseDeFix))
                continue;

            var linea = ((IXmlLineInfo)columna).HasLineInfo() ? ((IXmlLineInfo)columna).LineNumber : -1;
            ofensores.Add($"{rutaRelativa}:{linea} (columna \"{columna.Attribute("Header")?.Value}\", " +
                $"control <{controlSospechoso.Name.LocalName}>)");
        }

        return ofensores;
    }

    /// <summary>Sube desde el directorio de build hasta encontrar StockApp.sln -- mismo patrón
    /// que RelojDeParedEnBuclesDeEsperaGuardTests.ResolverRaizDelRepo.</summary>
    private static string ResolverRaizDelRepo()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !File.Exists(Path.Combine(directorio.FullName, "StockApp.sln")))
            directorio = directorio.Parent;

        if (directorio is null)
        {
            throw new InvalidOperationException(
                "No se encontró StockApp.sln subiendo desde el directorio de build.");
        }

        return directorio.FullName;
    }
}
