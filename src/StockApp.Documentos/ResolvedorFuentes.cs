using PdfSharp.Fonts;

namespace StockApp.Documentos;

/// <summary>
/// Resuelve fuentes para MigraDoc/PDFsharp a partir de un TTF embebido como recurso del
/// ensamblado, sin depender de fuentes instaladas en el sistema operativo.
/// </summary>
/// <remarks>
/// Hallazgo de la Tarea 0 (spike, docs/superpowers/decisions/2026-09-18-spike-pdf-imagenes-y-assert.md):
/// <c>PDFsharp-MigraDoc</c> 6.2.4 (paquete base, sin GDI+) no resuelve NINGUNA fuente en NINGUNA
/// plataforma (ni Windows ni Linux) sin un <see cref="IFontResolver"/> explícito asignado a
/// <see cref="GlobalFontSettings.FontResolver"/> — revienta con
/// "No appropriate font found" antes incluso de renderizar la primera palabra. Esto no lo
/// contemplaba el plan original; se resuelve acá, en la Tarea 1, porque bloquea a toda tarea
/// que renderice texto (es decir, todas las siguientes).
///
/// Fuente elegida: Inter (SIL Open Font License 1.1), release oficial v4.1
/// (https://github.com/rsms/inter/releases/tag/v4.1). Licencia verificada contra el
/// <c>LICENSE.txt</c> del release oficial antes de embeberla (copia en
/// <c>Fuentes/LICENSE-Inter.txt</c>): permite uso, embebido, redistribución y bundling con
/// software sin costo, mientras no se venda la fuente por sí sola ni se reutilicen sus nombres
/// reservados en derivados — ninguna de las dos cosas aplica acá. Mismo criterio ya usado por
/// la app de escritorio vía <c>Avalonia.Fonts.Inter</c>, así que el PDF queda visualmente
/// coherente con la UI.
///
/// Toda familia pedida (sea cual sea el nombre que use MigraDoc/el documento) se resuelve a
/// Inter Regular o Inter Bold según <paramref name="isBold"/> — no hay variantes itálicas ni
/// mapeo por nombre de familia. Es una decisión deliberada de alcance para esta tarea de
/// scaffolding: los documentos de esta app no necesitan monoespaciada ni itálica, y agregar
/// variantes no usadas sería sobre-ingeniería.
/// </remarks>
public sealed class ResolvedorFuentes : IFontResolver
{
    private const string CaraRegular = "Inter-Regular";
    private const string CaraBold = "Inter-Bold";

    /// <summary>Nombre de familia a usar en el documento MigraDoc (p. ej. <c>Font.Name</c>).</summary>
    public const string NombreFamilia = "Inter";

    public byte[] GetFont(string faceName) => faceName switch
    {
        CaraRegular => LeerRecursoEmbebido("Inter-Regular.ttf"),
        CaraBold => LeerRecursoEmbebido("Inter-Bold.ttf"),
        _ => throw new ArgumentException($"Cara de fuente no reconocida: '{faceName}'.", nameof(faceName)),
    };

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? CaraBold : CaraRegular);

    private static byte[] LeerRecursoEmbebido(string nombreArchivo)
    {
        var assembly = typeof(ResolvedorFuentes).Assembly;
        var nombreRecurso = $"{assembly.GetName().Name}.Fuentes.{nombreArchivo}";
        using var stream = assembly.GetManifestResourceStream(nombreRecurso)
            ?? throw new InvalidOperationException(
                $"No se encontró el recurso embebido '{nombreRecurso}' en el ensamblado " +
                $"'{assembly.GetName().Name}'. Verificar el <EmbeddedResource>/<LogicalName> en " +
                "StockApp.Documentos.csproj.");

        using var memoria = new MemoryStream();
        stream.CopyTo(memoria);
        return memoria.ToArray();
    }
}
