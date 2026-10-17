using System.Collections.Generic;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Fila uniforme de la grilla genérica de catálogos: la vista tiene columnas fijas bindeadas a
/// <see cref="Valor0"/> a <see cref="Valor4"/> (bindings compilados) y cada catálogo decide qué valor va en cada una.
/// </summary>
public sealed class FilaCatalogo
{
    public FilaCatalogo(object entidad, bool activo, IReadOnlyList<string?> valores)
    {
        Entidad = entidad;
        Activo  = activo;
        Valor0  = valores.Count > 0 ? valores[0] ?? string.Empty : string.Empty;
        Valor1  = valores.Count > 1 ? valores[1] : null;
        Valor2  = valores.Count > 2 ? valores[2] : null;
        Valor3  = valores.Count > 3 ? valores[3] : null;
        Valor4  = valores.Count > 4 ? valores[4] : null;
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
}
