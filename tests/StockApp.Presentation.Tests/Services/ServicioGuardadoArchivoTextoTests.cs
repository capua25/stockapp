using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Guardián de regresión (Task 8, export PDF): el camino de TEXTO de <see cref="ServicioGuardadoArchivo"/>
/// (el que usa el CSV hoy) NO debe cambiar de comportamiento al agregar <c>extension</c>/<c>tipoMime</c>
/// al camino de BYTES. Este test fija la construcción de <c>FilePickerSaveOptions</c> del guardado de
/// texto: sigue ofreciendo únicamente el filtro ".csv" con tipo MIME "text/csv", exactamente como
/// antes de esta tarea (ver <see cref="ServicioGuardadoArchivo.ConstruirOpcionesGuardadoTexto"/>).
/// </summary>
public class ServicioGuardadoArchivoTextoTests
{
    [Fact]
    public void ConstruirOpcionesGuardadoTexto_SigueOfreciendoSoloCsv()
    {
        var opciones = ServicioGuardadoArchivo.ConstruirOpcionesGuardadoTexto("valorizacion");

        Assert.Equal("valorizacion", opciones.SuggestedFileName);
        Assert.Equal("csv", opciones.DefaultExtension);

        var tipo = Assert.Single(opciones.FileTypeChoices!);
        Assert.Equal("Archivo CSV", tipo.Name);
        Assert.Equal(new[] { "*.csv" }, tipo.Patterns);
        Assert.Equal(new[] { "text/csv" }, tipo.MimeTypes);
    }
}
