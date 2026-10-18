using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StockApp.Presentation.ViewModels.Administracion;

/// <summary>
/// Grupo del master de Catálogos (INVENTARIO, FINANZAS, TAREAS). Su visibilidad es un getter sobre la sesión:
/// la revocación de un permiso en caliente la re-evalúa con <see cref="Recalcular"/>.
/// </summary>
public sealed class GrupoCatalogo : ObservableObject
{
    private readonly Func<bool> _visible;

    public GrupoCatalogo(string titulo, IReadOnlyList<EntradaCatalogo> entradas, Func<bool> visible)
    {
        Titulo   = titulo;
        Entradas = entradas;
        _visible = visible;
    }

    public string Titulo { get; }
    public IReadOnlyList<EntradaCatalogo> Entradas { get; }
    public bool EsVisible => _visible();

    public void Recalcular() => OnPropertyChanged(nameof(EsVisible));
}
