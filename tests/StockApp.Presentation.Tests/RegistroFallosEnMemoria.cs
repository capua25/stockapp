using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.Tests;

/// <summary>
/// Doble en memoria de <see cref="IRegistroFallos"/> para toda la suite (fix 2026-08-20):
/// instalado UNA vez por <see cref="TestBootstrap"/> vía [ModuleInitializer] para que
/// RefrescoPermisos.DispararBestEffortAsync deje de escribir en el crash.log real durante
/// `dotnet test`. ConcurrentQueue porque xUnit corre colecciones de test en paralelo por
/// default y esta instancia es compartida por toda la suite -- una List&lt;T&gt; corriente no
/// es thread-safe y podría tirar bajo escritura concurrente.
/// </summary>
public sealed class RegistroFallosEnMemoria : IRegistroFallos
{
    private readonly ConcurrentQueue<(string Origen, Exception Ex)> _entradas = new();

    public IReadOnlyCollection<(string Origen, Exception Ex)> Entradas => _entradas;

    public void LogFatal(string origen, Exception ex) => _entradas.Enqueue((origen, ex));
}
