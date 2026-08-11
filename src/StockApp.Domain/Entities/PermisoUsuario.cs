namespace StockApp.Domain.Entities;

/// <summary>
/// Un permiso configurable concedido a un usuario Operador. VERDAD ÚNICA de los permisos
/// configurables (spec 2026-08-10, decisión 3): resolver = SELECT, sin merge ni overrides.
/// Sin fila para un (UsuarioId, Permiso) = ese permiso no está concedido (fail-closed).
/// Nunca existen filas para los 4 permisos estructurales (GestionarUsuarios, ImportarPlanillas,
/// GestionarDiagnostico, AdministrarTareas) — esos nunca se resuelven contra esta tabla.
/// </summary>
public class PermisoUsuario
{
    public int Id { get; set; }

    /// <summary>FK a Usuarios.Id, Restrict (mismo criterio que toda otra FK hacia Usuarios:
    /// la baja es lógica, nunca DELETE físico, así que no hay cascada que propagar).</summary>
    public int UsuarioId { get; set; }

    /// <summary>Uno de los 11 permisos configurables de Permisos.cs (ej. "finanzas.ver").</summary>
    public string Permiso { get; set; } = string.Empty;

    public Usuario? Usuario { get; set; }
}
