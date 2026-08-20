using System;

namespace StockApp.Presentation.Services;

/// <summary>
/// Abstracción del registro de fallos "best-effort" (crash.log en producción). Introducida
/// para que <see cref="RefrescoPermisos"/> deje de llamar directo a
/// <see cref="Program.LogFatal(string, Exception)"/> — sin esto, cada corrida de
/// `dotnet test` terminaba escribiendo en el crash.log real del usuario (miles de entradas
/// acumuladas, ver fix 2026-08-20). La implementación de producción es
/// <see cref="RegistroFallosArchivo"/>; los tests usan un doble en memoria.
/// </summary>
public interface IRegistroFallos
{
    void LogFatal(string origen, Exception ex);
}
