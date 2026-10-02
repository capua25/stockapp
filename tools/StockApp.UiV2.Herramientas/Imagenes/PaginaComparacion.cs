using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace StockApp.UiV2.Herramientas.Imagenes;

/// <summary>
/// Página de aprobación de una pantalla (spec §6.2.3): mockup, app y diferencias lado a lado,
/// más el índice de todas las pantallas comparadas. Vive en artifacts/ (ignorado por git).
/// </summary>
public static class PaginaComparacion
{
    public const string ArchivoResultado = "resultado.json";

    /// <summary>
    /// Recortes tiene Equals/GetHashCode a mano: el record generado compara IReadOnlyList&lt;string&gt;
    /// por referencia (List&lt;T&gt; no tiene igualdad estructural), y eso rompía la comparación por
    /// valor de dos resultados con la misma lista de recortes pero instancias distintas.
    /// </summary>
    public sealed record ResultadoPersistido(
        string Pantalla, int PixelesDistintos, double Porcentaje, int TeselasDensas, double UmbralPorcentaje,
        int TeselasDensasMax, IReadOnlyList<string>? Recortes = null)
    {
        public IReadOnlyList<string> Recortes { get; init; } = Recortes ?? [];

        public bool Equals(ResultadoPersistido? other) =>
            other is not null
            && Pantalla == other.Pantalla
            && PixelesDistintos == other.PixelesDistintos
            && Porcentaje.Equals(other.Porcentaje)
            && TeselasDensas == other.TeselasDensas
            && UmbralPorcentaje.Equals(other.UmbralPorcentaje)
            && TeselasDensasMax == other.TeselasDensasMax
            && Recortes.SequenceEqual(other.Recortes);

        public override int GetHashCode() =>
            HashCode.Combine(Pantalla, PixelesDistintos, Porcentaje, TeselasDensas, UmbralPorcentaje, TeselasDensasMax);
    }

    /// <summary>
    /// I2 (revisión final): los píxeles no ven un glifo recortado (spec §6.5) — si hay algún
    /// recorte de texto, la pantalla queda RECHAZADA sin importar qué tan bien midan los píxeles.
    /// </summary>
    public static bool Aprobada(
        ResultadoComparacion r, double umbralPorcentaje, int teselasDensasMax, IReadOnlyCollection<string>? recortes = null) =>
        (recortes is null || recortes.Count == 0)
        && r.PorcentajeDistinto <= umbralPorcentaje
        && r.TeselasDensas <= teselasDensasMax;

    public static string Escribir(
        string carpetaPantalla, string pantalla, ResultadoComparacion r, double umbralPorcentaje, int teselasDensasMax,
        IReadOnlyCollection<string>? recortes = null)
    {
        recortes ??= [];
        var aprobada = Aprobada(r, umbralPorcentaje, teselasDensasMax, recortes);
        var titulo = WebUtility.HtmlEncode(pantalla);
        var seccionRecortes = recortes.Count == 0
            ? ""
            : "<p class=\"rechazada\">Texto recortado por el layout (la comparación de píxeles no lo detecta):</p><ul>"
              + string.Concat(recortes.Select(t => $"<li>{WebUtility.HtmlEncode(t)}</li>")) + "</ul>";
        var html = $$"""
            <!doctype html>
            <html lang="es">
            <head>
            <meta charset="utf-8">
            <title>{{titulo}}: comparación</title>
            <style>
              body { margin: 16px; font-family: sans-serif; color: #1C1917; background: #FFFFFF; }
              .aprobada { color: #14532D; font-weight: 600; }
              .rechazada { color: #991B1B; font-weight: 600; }
              .fila { display: flex; gap: 16px; align-items: flex-start; }
              figure { margin: 0; flex: 1 1 0; min-width: 0; }
              figcaption { font-size: 13px; margin-bottom: 4px; }
              img { width: 100%; border: 1px solid #E7E5E4; }
            </style>
            </head>
            <body>
            <h1>{{titulo}}</h1>
            <p class="{{(aprobada ? "aprobada" : "rechazada")}}">{{(aprobada ? "APROBADA" : "RECHAZADA")}}</p>
            <p>Píxeles distintos: {{r.PixelesDistintos}} de {{r.Ancho * r.Alto}} ({{F(r.PorcentajeDistinto)}}%, umbral {{F(umbralPorcentaje)}}%).
               Teselas densas: {{r.TeselasDensas}} (máximo {{teselasDensasMax}}).</p>
            {{seccionRecortes}}
            <p><a href="../index.html">Todas las pantallas</a>. Click en una imagen para verla a tamaño real.
               En el diff: rojo = diferencia fuerte, naranja = diferencia leve de color, contorno magenta = tesela densa.</p>
            <div class="fila">
              <figure><figcaption>Mockup</figcaption><a href="mockup.png"><img src="mockup.png" alt="mockup"></a></figure>
              <figure><figcaption>App (render headless)</figcaption><a href="app.png"><img src="app.png" alt="app"></a></figure>
              <figure><figcaption>Diferencias</figcaption><a href="diff.png"><img src="diff.png" alt="diferencias"></a></figure>
            </div>
            </body>
            </html>
            """;

        var rutaPagina = Path.Combine(carpetaPantalla, "index.html");
        File.WriteAllText(rutaPagina, html);
        File.WriteAllText(
            Path.Combine(carpetaPantalla, ArchivoResultado),
            JsonSerializer.Serialize(new ResultadoPersistido(
                pantalla, r.PixelesDistintos, r.PorcentajeDistinto, r.TeselasDensas, umbralPorcentaje, teselasDensasMax,
                recortes.ToList())));
        EscribirIndice(Path.GetDirectoryName(Path.GetFullPath(carpetaPantalla))!);
        return rutaPagina;
    }

    /// <summary>
    /// I2b (revisión final): ComparacionVisual llama esto ANTES de intentar capturar. Si
    /// CapturadorMockup.CapturarAsync lanza, el código nunca llega a Escribir — sin este borrado
    /// previo, el resultado.json (y la fila del índice) de la corrida anterior seguían vigentes y
    /// una pantalla rota podía quedar listada como APROBADA indefinidamente.
    /// </summary>
    public static void BorrarResultadoAnterior(string carpetaPantalla)
    {
        var archivoResultado = Path.Combine(carpetaPantalla, ArchivoResultado);
        var archivoPagina = Path.Combine(carpetaPantalla, "index.html");
        var huboAlgoQueBorrar = File.Exists(archivoResultado) || File.Exists(archivoPagina);
        if (File.Exists(archivoResultado))
        {
            File.Delete(archivoResultado);
        }

        if (File.Exists(archivoPagina))
        {
            File.Delete(archivoPagina);
        }

        if (huboAlgoQueBorrar)
        {
            EscribirIndice(Path.GetDirectoryName(Path.GetFullPath(carpetaPantalla))!);
        }
    }

    /// <summary>
    /// Task 5b: una fila por pantalla y tamaño, agrupadas por pantalla (estándar primero). El
    /// tamaño sale del sufijo de la carpeta ("&lt;pantalla&gt;@1366x768", ver
    /// <see cref="TamanoLienzo.SufijoPantalla"/>); sin sufijo es el estándar. El orden ordinal de las
    /// carpetas no alcanza: "login-error" caería entre "login" y "login@1366x768".
    /// </summary>
    private static void EscribirIndice(string carpetaRaiz)
    {
        var filas = new StringBuilder();
        var carpetas = Directory.GetDirectories(carpetaRaiz)
            .Select(d => (Carpeta: d, Partes: Path.GetFileName(d).Split('@', 2)))
            .Select(x => (x.Carpeta, Pantalla: x.Partes[0], Tamano: x.Partes.Length == 2 ? x.Partes[1] : TamanoLienzo.Estandar.ToString()))
            .OrderBy(x => x.Pantalla, StringComparer.Ordinal)
            .ThenBy(x => x.Tamano == TamanoLienzo.Estandar.ToString() ? 0 : 1)
            .ThenBy(x => x.Tamano, StringComparer.Ordinal);
        foreach (var (carpeta, pantalla, tamano) in carpetas)
        {
            var archivo = Path.Combine(carpeta, ArchivoResultado);
            if (!File.Exists(archivo))
            {
                continue;
            }

            var p = JsonSerializer.Deserialize<ResultadoPersistido>(File.ReadAllText(archivo))!;
            var aprobada = p.Recortes.Count == 0 && p.Porcentaje <= p.UmbralPorcentaje && p.TeselasDensas <= p.TeselasDensasMax;
            var nombre = Path.GetFileName(carpeta);
            filas.Append($"<tr><td><a href=\"{Uri.EscapeDataString(nombre)}/index.html\">{WebUtility.HtmlEncode(pantalla)}</a></td>")
                 .Append($"<td>{WebUtility.HtmlEncode(tamano)}</td>")
                 .Append($"<td>{F(p.Porcentaje)}%</td><td>{p.TeselasDensas}</td>")
                 .Append($"<td class=\"{(aprobada ? "aprobada" : "rechazada")}\">{(aprobada ? "APROBADA" : "RECHAZADA")}</td></tr>\n");
        }

        File.WriteAllText(Path.Combine(carpetaRaiz, "index.html"), $$"""
            <!doctype html>
            <html lang="es">
            <head>
            <meta charset="utf-8">
            <title>Comparaciones UI v2</title>
            <style>
              body { margin: 16px; font-family: sans-serif; color: #1C1917; }
              td, th { padding: 4px 12px; text-align: left; border-bottom: 1px solid #F0EFED; }
              .aprobada { color: #14532D; font-weight: 600; }
              .rechazada { color: #991B1B; font-weight: 600; }
            </style>
            </head>
            <body>
            <h1>Comparaciones UI v2</h1>
            <table>
            <tr><th>Pantalla</th><th>Tamaño</th><th>Píxeles distintos</th><th>Teselas densas</th><th>Veredicto</th></tr>
            {{filas}}</table>
            </body>
            </html>
            """);
    }

    private static string F(double valor) => valor.ToString("0.000", CultureInfo.InvariantCulture);
}
