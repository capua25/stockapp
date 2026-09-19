using StockApp.Application.Exportacion;

namespace StockApp.Documentos;

/// <summary>
/// Implementación de <see cref="IPdfExporter"/> basada en MigraDoc. Delega toda la
/// construcción del documento a <see cref="PlantillaTabular"/> -- esta clase es solo el punto
/// de entrada que se registra en DI (Tarea 9).
/// </summary>
public sealed class PdfExporterMigraDoc : IPdfExporter
{
    private readonly PlantillaTabular _plantilla = new();

    public byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<ColumnaPdf> columnas, MetadatosDocumento metadatos)
        => _plantilla.Generar(items, columnas, metadatos);
}
