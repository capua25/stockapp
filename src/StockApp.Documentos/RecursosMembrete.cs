namespace StockApp.Documentos;

/// <summary>
/// Logo institucional para el membrete, embebido DENTRO de StockApp.Documentos (no en
/// StockApp.Presentation/Assets): este proyecto no depende de Avalonia (ver Restricciones
/// globales del plan), así que el recurso vive como <c>EmbeddedResource</c> del propio
/// ensamblado -- StockApp.Documentos queda autocontenido y testeable sin levantar Avalonia.
/// Variante negra (no la de fondo con transparencia usada en Login/Inicio): sobre papel blanco
/// corresponde el logo negro, no el de marca de agua.
///
/// Leído con <see cref="System.Reflection.Assembly.GetManifestResourceStream(string)"/>, nunca
/// del filesystem: la Tarea 0 leyó fuentes desde <c>/usr/share/fonts/...</c> en el spike, ruta
/// que no existe en el Windows de producción. El mismo error no se repite acá.
/// </summary>
internal static class RecursosMembrete
{
    private const string RutaRecurso = "StockApp.Documentos.Assets.carmelo-negro.png";

    internal static byte[] ObtenerLogoNegro()
    {
        var assembly = typeof(RecursosMembrete).Assembly;
        using var stream = assembly.GetManifestResourceStream(RutaRecurso)
            ?? throw new InvalidOperationException(
                $"No se encontró el recurso embebido '{RutaRecurso}' en el ensamblado " +
                $"'{assembly.GetName().Name}'. Verificar el <EmbeddedResource>/<LogicalName> en " +
                "StockApp.Documentos.csproj.");

        using var memoria = new MemoryStream();
        stream.CopyTo(memoria);
        return memoria.ToArray();
    }
}
