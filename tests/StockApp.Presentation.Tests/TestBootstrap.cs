using System.Runtime.CompilerServices;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.Tests;

/// <summary>
/// Bootstrap de la suite (fix 2026-08-20). [ModuleInitializer] garantiza que esto corra UNA
/// vez, antes de que se ejecute cualquier test de este assembly (el runtime lo dispara al
/// cargar el módulo) -- sin esto, RefrescoPermisos.DispararBestEffortAsync usaba su default de
/// producción (RegistroFallosArchivo) y cada corrida de `dotnet test` escribía en el crash.log
/// real del usuario (evidencia: 5175 entradas / 4 MB acumuladas). Ver el equivalente en
/// StockApp.Presentation.UiTests.
/// </summary>
internal static class TestBootstrap
{
    [ModuleInitializer]
    internal static void Inicializar() =>
        RefrescoPermisos.ConfigurarRegistroFallos(new RegistroFallosEnMemoria());
}
