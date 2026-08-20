using System.Runtime.CompilerServices;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Bootstrap de este assembly (fix 2026-08-20) -- ver el equivalente y la explicación completa
/// en tests/StockApp.Presentation.Tests/TestBootstrap.cs. [ModuleInitializer] corre una única
/// vez al cargar el módulo, antes de cualquier [AvaloniaFact]/[Fact] de este proyecto.
/// </summary>
internal static class TestBootstrap
{
    [ModuleInitializer]
    internal static void Inicializar() =>
        RefrescoPermisos.ConfigurarRegistroFallos(new RegistroFallosEnMemoria());
}
