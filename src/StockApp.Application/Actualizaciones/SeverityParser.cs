namespace StockApp.Application.Actualizaciones;

/// <summary>Parsea la primera línea `severity: &lt;valor&gt;` de las release notes markdown.
/// Default Normal si ausente o inválido. Cero dependencia de infraestructura.</summary>
public class SeverityParser
{
    public UpdateSeverity Parse(string? notasMarkdown)
    {
        if (string.IsNullOrWhiteSpace(notasMarkdown))
            return UpdateSeverity.Normal;

        var primera = notasMarkdown
            .Split('\n')[0]
            .Trim();

        if (!primera.StartsWith("severity:", StringComparison.OrdinalIgnoreCase))
            return UpdateSeverity.Normal;

        var valor = primera["severity:".Length..].Trim();
        return valor.ToLowerInvariant() switch
        {
            "critical"  => UpdateSeverity.Critical,
            "important" => UpdateSeverity.Important,
            _           => UpdateSeverity.Normal,
        };
    }
}
