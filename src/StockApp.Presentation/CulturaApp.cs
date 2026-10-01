using System.Globalization;
using StockApp.Domain.Formato;

namespace StockApp.Presentation;

/// <summary>
/// Fija la cultura por defecto del proceso del desktop en es-UY, construida desde el punto de
/// verdad <see cref="FormatoEsUy"/> (decisión 2026-10-01: único formato de la app, no
/// configurable). Antes el proceso no fijaba ninguna cultura: cualquier <c>StringFormat</c> o
/// interpolación sin cultura explícita salía con la del SO (punto en WSL, coma en Windows es-UY).
/// Los converters igual formatean con <see cref="FormatoEsUy"/> sin depender de esto; esto es la
/// red para lo que se escape.
/// </summary>
public static class CulturaApp
{
    public static void Aplicar()
    {
        var cultura = FormatoEsUy.CrearCultura();
        CultureInfo.DefaultThreadCurrentCulture = cultura;
        CultureInfo.DefaultThreadCurrentUICulture = cultura;
        CultureInfo.CurrentCulture = cultura;
        CultureInfo.CurrentUICulture = cultura;
    }
}
