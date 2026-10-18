using System;
using System.Collections.Generic;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Fila uniforme de la grilla genérica de catálogos: la vista tiene columnas fijas bindeadas a
/// <see cref="Valor0"/> a <see cref="Valor4"/> (bindings compilados) y cada catálogo decide qué valor va en cada una.
/// </summary>
public sealed class FilaCatalogo
{
    public FilaCatalogo(object entidad, bool activo, IReadOnlyList<string?> valores, IReadOnlyList<IComparable?>? ordenes = null)
    {
        Entidad = entidad;
        Activo  = activo;
        Valor0  = valores.Count > 0 ? valores[0] ?? string.Empty : string.Empty;
        Valor1  = valores.Count > 1 ? valores[1] : null;
        Valor2  = valores.Count > 2 ? valores[2] : null;
        Valor3  = valores.Count > 3 ? valores[3] : null;
        Valor4  = valores.Count > 4 ? valores[4] : null;
        // Clave de orden de cada columna: la que declara el catálogo (un número, una fecha) o, si no, el propio texto.
        Orden1  = Clave(ordenes, 1, Valor1);
        Orden2  = Clave(ordenes, 2, Valor2);
        Orden3  = Clave(ordenes, 3, Valor3);
        Orden4  = Clave(ordenes, 4, Valor4);
    }

    /// <summary>La entidad de dominio de la que sale la fila.</summary>
    public object Entidad { get; }

    public bool Activo { get; }

    /// <summary>El nombre.</summary>
    public string Valor0 { get; }

    public string? Valor1 { get; }
    public string? Valor2 { get; }
    public string? Valor3 { get; }
    public string? Valor4 { get; }

    /// <summary>Por qué se ordena cada columna medida: el texto ordenaría "10" antes que "3". Las usa el SortMemberPath de la grilla.</summary>
    public IComparable? Orden1 { get; }
    public IComparable? Orden2 { get; }
    public IComparable? Orden3 { get; }
    public IComparable? Orden4 { get; }

    private static IComparable? Clave(IReadOnlyList<IComparable?>? ordenes, int indice, string? texto)
        => ordenes is not null && indice < ordenes.Count && ordenes[indice] is { } clave ? clave : texto;
}
