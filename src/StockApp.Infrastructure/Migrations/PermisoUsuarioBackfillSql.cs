namespace StockApp.Infrastructure.Migrations;

/// <summary>
/// SQL de backfill de PermisosUsuario para Operadores preexistentes, extraído a una constante
/// compartida entre la migración (AgregaPermisoUsuario.Up) y el test de Infrastructure
/// (PermisoUsuarioBackfillTests) — evita que ambos textos diverjan con el tiempo. Orden y
/// contenido: exactamente los 9 permisos que hoy tiene AuthorizationService.PermisosInicialesOperador
/// (antes AccionesOperador), en el mismo orden textual del archivo — no el orden de iteración
/// de un HashSet, que no está garantizado (mismo criterio que la corrección aplicada al backfill
/// de LotesImportacion, commit af4321b). VerReportes y GestionarTablasMaestras quedan afuera
/// a propósito: hoy ningún Operador los tiene.
/// </summary>
public static class PermisoUsuarioBackfillSql
{
    public const string InsertarPermisosIniciales =
        """
        INSERT INTO "PermisosUsuario" ("UsuarioId", "Permiso")
        SELECT u."Id", p.permiso
        FROM "Usuarios" u
        CROSS JOIN (VALUES
            ('catalogo.productos'), ('movimientos.registrar'), ('stock.recalcular'),
            ('finanzas.ver'), ('finanzas.maestros'), ('finanzas.gastos'),
            ('finanzas.pagos'), ('finanzas.ingresos'), ('tareas.gestionar')
        ) AS p(permiso)
        WHERE u."Rol" = 1
        ON CONFLICT ("UsuarioId", "Permiso") DO NOTHING;
        """;
}
