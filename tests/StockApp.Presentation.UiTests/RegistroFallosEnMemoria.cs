using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Doble en memoria de <see cref="IRegistroFallos"/> para este assembly (fix 2026-08-20).
/// Copia local de tests/StockApp.Presentation.Tests/RegistroFallosEnMemoria.cs -- ambos
/// proyectos de test son assemblies independientes (Tests usa xUnit v2, UiTests xUnit v3) y no
/// se referencian entre sí, así que no hay un lugar compartido natural para esta clase de ~10
/// líneas sin crear un proyecto nuevo solo para esto.
/// </summary>
public sealed class RegistroFallosEnMemoria : IRegistroFallos
{
    private readonly ConcurrentQueue<(string Origen, Exception Ex)> _entradas = new();

    public IReadOnlyCollection<(string Origen, Exception Ex)> Entradas => _entradas;

    public void LogFatal(string origen, Exception ex) => _entradas.Enqueue((origen, ex));
}
