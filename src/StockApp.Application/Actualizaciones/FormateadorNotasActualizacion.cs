using System;
using System.Collections.Generic;
using System.Linq;

namespace StockApp.Application.Actualizaciones;

/// <summary>Limpia el markdown crudo de las release notes para mostrarlo en la UI:
/// remueve la línea de <c>severity:</c>, corta el bloque de instrucciones internas del
/// publicador (todo lo que sigue a un separador <c>---</c>) y normaliza el formato markdown
/// básico (títulos, bullets) a texto legible. Determinista, sin dependencias externas.</summary>
public static class FormateadorNotasActualizacion
{
    public static string Limpiar(string? notasMarkdown)
    {
        if (string.IsNullOrWhiteSpace(notasMarkdown))
            return string.Empty;

        var lineas = notasMarkdown.Replace("\r\n", "\n").Split('\n').ToList();

        // (a) elimina la primera línea si es la marca de severity.
        if (lineas.Count > 0 && lineas[0].Trim().StartsWith("severity:", StringComparison.OrdinalIgnoreCase))
            lineas.RemoveAt(0);

        // (b) corta todo desde una línea que sea exactamente "---" en adelante
        //     (ahí empieza el bloque de instrucciones internas del publicador).
        var indiceSeparador = lineas.FindIndex(l => l.Trim() == "---");
        if (indiceSeparador >= 0)
            lineas = lineas.Take(indiceSeparador).ToList();

        // (c) normaliza prefijos markdown de inicio de línea (títulos, bullets).
        var procesadas = lineas.Select(NormalizarLinea).ToList();

        // (d) colapsa líneas en blanco múltiples consecutivas.
        var resultado = new List<string>();
        var blancoAnterior = false;
        foreach (var linea in procesadas)
        {
            var esBlanco = string.IsNullOrWhiteSpace(linea);
            if (esBlanco && blancoAnterior)
                continue;

            resultado.Add(linea);
            blancoAnterior = esBlanco;
        }

        return string.Join('\n', resultado).Trim('\n', ' ');
    }

    private static string NormalizarLinea(string linea)
    {
        var recortada = linea.TrimStart();
        var indentacion = linea[..(linea.Length - recortada.Length)];

        if (recortada.StartsWith('#'))
        {
            var texto = recortada.TrimStart('#').Trim();
            return indentacion + texto;
        }

        if (recortada.StartsWith("- ") || recortada.StartsWith("* "))
        {
            var texto = recortada[2..].Trim();
            return indentacion + "• " + texto;
        }

        return linea;
    }
}
