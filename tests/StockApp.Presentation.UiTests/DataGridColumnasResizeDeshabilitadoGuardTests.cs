using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardián DINÁMICO de repositorio: impide que cualquier .axaml de Views/ vuelva a deshabilitar
/// el resize de columnas que Themes/DataGrid.axaml habilita GLOBAL (ver
/// <see cref="DataGridColumnasRedimensionablesTests"/> para la evidencia decompilada del default
/// real de Avalonia.Controls.DataGrid 12.0.1, que es false).
///
/// QUÉ CUBRE: recorre TODOS los .axaml de src/StockApp.Presentation/Views/ (barrido dinámico,
/// ningún archivo hardcodeado) y falla si aparece
///   <c>CanUserResizeColumns="False"</c> en un elemento &lt;DataGrid&gt;, o
///   <c>CanUserResize="False"</c> en cualquier columna (DataGridTextColumn/DataGridTemplateColumn/
///   DataGridCheckBoxColumn/cualquier tipo, no una lista cerrada: cualquier elemento cuyo nombre
///   termine en "Column" y no sea DataGrid.Columns).
/// Ambos casos apagarían el resize efectivo para esa grilla/columna puntual sin que el Style
/// global de Themes/DataGrid.axaml pueda revertirlo (CanUserResizeInternal, si está fijado a
/// mano, gana siempre sobre el default heredado del DataGrid dueño).
///
/// Parseo por XML real (XDocument), no regex línea a línea, mismo criterio que
/// <c>DataGridCeldaConControlPaddingPropioGuardTests</c>: acá hace falta distinguir el nombre del
/// elemento (DataGrid vs. una columna) del atributo, y un regex por línea podría confundir un
/// comentario o un binding que mencione el mismo texto.
/// </summary>
public class DataGridColumnasResizeDeshabilitadoGuardTests
{
    [Fact]
    public void NingunaGrillaNiColumna_DeshabilitaElResizeExplicitamente()
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
            ofensores.AddRange(OfensoresEnEsteArchivo(archivo, raiz));

        Assert.True(ofensores.Count == 0,
            "Grilla(s) o columna(s) de DataGrid con el resize deshabilitado explícitamente " +
            "(CanUserResizeColumns=\"False\" en el DataGrid, o CanUserResize=\"False\" en una " +
            "columna). El pedido es que TODAS las columnas de TODAS las grillas de la app sean " +
            "redimensionables por el usuario -- Themes/DataGrid.axaml ya lo habilita global, así " +
            "que un atributo puntual como este lo estaría revirtiendo para esa grilla/columna. Si " +
            "hay una razón real para esta excepción, documentala explícitamente en el XAML (sin " +
            "'--' dentro del comentario, rompe el build) en vez de sacarla en silencio. " +
            "Ofensores: " + string.Join("; ", ofensores));
    }

    /// <summary>obj/ y bin/ pueden traer copias generadas que no son fuente versionada -- mismo
    /// criterio que RelojDeParedEnBuclesDeEsperaGuardTests.EsArchivoFuenteReal.</summary>
    private static bool EsArchivoFuenteReal(string ruta)
    {
        var segmentos = ruta.Split(Path.DirectorySeparatorChar);
        return !segmentos.Contains("obj") && !segmentos.Contains("bin");
    }

    private static IEnumerable<string> OfensoresEnEsteArchivo(string ruta, string raiz)
    {
        XDocument documento;
        try
        {
            documento = XDocument.Load(ruta, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            // Un .axaml que no parsea como XML es un problema del propio archivo (o del guardián
            // apuntando a algo que no debería), no algo que este test deba silenciar.
            return new[] { $"{Path.GetRelativePath(raiz, ruta)}: no se pudo parsear como XML ({ex.Message})" };
        }

        var rutaRelativa = Path.GetRelativePath(raiz, ruta);
        var ofensores = new List<string>();

        foreach (var grilla in documento.Descendants().Where(e => e.Name.LocalName == "DataGrid"))
        {
            if (!string.Equals(grilla.Attribute("CanUserResizeColumns")?.Value, "False",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var linea = ((IXmlLineInfo)grilla).HasLineInfo() ? ((IXmlLineInfo)grilla).LineNumber : -1;
            ofensores.Add($"{rutaRelativa}:{linea} (<DataGrid CanUserResizeColumns=\"False\">)");
        }

        // Cualquier elemento "...Column" (DataGridTextColumn, DataGridTemplateColumn,
        // DataGridCheckBoxColumn, custom, etc.) que no sea el contenedor DataGrid.Columns.
        foreach (var columna in documento.Descendants()
                     .Where(e => e.Name.LocalName.EndsWith("Column", StringComparison.Ordinal)
                                 && e.Name.LocalName != "DataGrid.Columns"))
        {
            if (!string.Equals(columna.Attribute("CanUserResize")?.Value, "False",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var linea = ((IXmlLineInfo)columna).HasLineInfo() ? ((IXmlLineInfo)columna).LineNumber : -1;
            ofensores.Add($"{rutaRelativa}:{linea} (<{columna.Name.LocalName} " +
                $"Header=\"{columna.Attribute("Header")?.Value}\" CanUserResize=\"False\">)");
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
