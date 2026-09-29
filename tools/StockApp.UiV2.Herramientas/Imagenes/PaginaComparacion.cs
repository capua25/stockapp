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

    public sealed record ResultadoPersistido(
        string Pantalla, int PixelesDistintos, double Porcentaje, int TeselasDensas, double UmbralPorcentaje, int TeselasDensasMax);

    public static bool Aprobada(ResultadoComparacion r, double umbralPorcentaje, int teselasDensasMax) =>
        r.PorcentajeDistinto <= umbralPorcentaje && r.TeselasDensas <= teselasDensasMax;

    public static string Escribir(
        string carpetaPantalla, string pantalla, ResultadoComparacion r, double umbralPorcentaje, int teselasDensasMax)
    {
        var aprobada = Aprobada(r, umbralPorcentaje, teselasDensasMax);
        var titulo = WebUtility.HtmlEncode(pantalla);
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
                pantalla, r.PixelesDistintos, r.PorcentajeDistinto, r.TeselasDensas, umbralPorcentaje, teselasDensasMax)));
        EscribirIndice(Path.GetDirectoryName(Path.GetFullPath(carpetaPantalla))!);
        return rutaPagina;
    }

    private static void EscribirIndice(string carpetaRaiz)
    {
        var filas = new StringBuilder();
        foreach (var carpeta in Directory.GetDirectories(carpetaRaiz).OrderBy(d => d, StringComparer.Ordinal))
        {
            var archivo = Path.Combine(carpeta, ArchivoResultado);
            if (!File.Exists(archivo))
            {
                continue;
            }

            var p = JsonSerializer.Deserialize<ResultadoPersistido>(File.ReadAllText(archivo))!;
            var aprobada = p.Porcentaje <= p.UmbralPorcentaje && p.TeselasDensas <= p.TeselasDensasMax;
            var nombre = Path.GetFileName(carpeta);
            filas.Append($"<tr><td><a href=\"{WebUtility.HtmlEncode(nombre)}/index.html\">{WebUtility.HtmlEncode(p.Pantalla)}</a></td>")
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
            <tr><th>Pantalla</th><th>Píxeles distintos</th><th>Teselas densas</th><th>Veredicto</th></tr>
            {{filas}}</table>
            </body>
            </html>
            """);
    }

    private static string F(double valor) => valor.ToString("0.000", CultureInfo.InvariantCulture);
}
