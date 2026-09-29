using System.Linq;
using System.Xml.Linq;

namespace StockApp.Tests.Compartido;

/// <summary>
/// Lee de App.axaml, en orden de documento, los Source de ResourceInclude y StyleInclude.
/// Compartido (ver csproj de UiTests/VisualTests vía Link) para que ambos bancos de test
/// comparen su TestApp contra el mismo parseo de la app real.
/// </summary>
internal static class IncludesDeAppAxaml
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    public static (string[] Recursos, string[] Estilos) Leer(string appAxaml)
    {
        var documento = XDocument.Parse(appAxaml);
        var recursos = documento.Descendants(Avalonia + "ResourceInclude")
            .Select(e => (string)e.Attribute("Source")!).ToArray();
        var estilos = documento.Descendants(Avalonia + "StyleInclude")
            .Select(e => (string)e.Attribute("Source")!).ToArray();
        return (recursos, estilos);
    }
}
