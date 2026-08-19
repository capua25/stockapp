using System.Collections.Generic;

namespace StockApp.Presentation.Services;

/// <summary>
/// Preferencias locales del menu lateral. Se persiste por MAQUINA y por usuario del sistema
/// operativo, no por usuario logueado y no en la base ni en la API.
/// </summary>
/// <param name="GruposAbiertos">Nombres de los grupos que quedaron desplegados.</param>
public record PreferenciasSidebar(IReadOnlyList<string> GruposAbiertos);
