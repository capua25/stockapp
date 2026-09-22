using StockApp.Application.Exportacion;
using Xunit;

namespace StockApp.Application.Tests.Exportacion;

public class MetadatosDocumentoTests
{
    [Fact]
    public void Constructor_GuardaLosTresValoresProvistos()
    {
        var metadatos = new MetadatosDocumento(
            Titulo: "Valorización de inventario",
            DescripcionFiltros: "Sin filtros aplicados.",
            UsuarioEmisor: "admin");

        Assert.Equal("Valorización de inventario", metadatos.Titulo);
        Assert.Equal("Sin filtros aplicados.", metadatos.DescripcionFiltros);
        Assert.Equal("admin", metadatos.UsuarioEmisor);
    }

    /// <summary>
    /// El contrato IPdfExporter debe poder implementarse con la firma exacta que van a usar
    /// las Tareas 3-20: genérico sobre T, recibe columnas + metadatos, devuelve byte[]. Este
    /// test compila (y falla si alguien cambia la firma) usando un fake local mínimo.
    /// </summary>
    private sealed record FilaDePrueba(string Nombre);

    private sealed class FakePdfExporter : IPdfExporter
    {
        public byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<ColumnaPdf> columnas, MetadatosDocumento metadatos, ResumenPdf? resumen = null)
            => new byte[] { 1, 2, 3 };
    }

    [Fact]
    public void IPdfExporter_SePuedeImplementarConLaFirmaEsperada()
    {
        IPdfExporter exportador = new FakePdfExporter();
        var items = new[] { new FilaDePrueba("x") };
        var metadatos = new MetadatosDocumento("t", "d", "u");

        var resultado = exportador.Exportar(
            items, new[] { new ColumnaPdf(nameof(FilaDePrueba.Nombre), "Nombre") }, metadatos);

        Assert.Equal(new byte[] { 1, 2, 3 }, resultado);
    }
}
