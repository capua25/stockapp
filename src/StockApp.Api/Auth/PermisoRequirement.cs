using Microsoft.AspNetCore.Authorization;

namespace StockApp.Api.Auth;

/// <summary>Requirement de un permiso concreto (spec 2026-08-10). Reemplaza el RequireClaim
/// fijo por rol: la policy sigue llamándose igual que el permiso (Permisos.X), así que los 32
/// endpoints existentes no cambian una sola línea de `.RequireAuthorization(Permisos.X)`.</summary>
public record PermisoRequirement(string Permiso) : IAuthorizationRequirement;
