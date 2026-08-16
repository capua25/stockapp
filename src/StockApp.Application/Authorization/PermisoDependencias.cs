namespace StockApp.Application.Authorization;

/// <summary>
/// Dependencia blanda: permiso recomendado + el mensaje que el panel le va a mostrar al Admin
/// tal cual (paso posterior del refactor). Separado de un simple string para que el mensaje
/// viaje pegado a la regla, en vez de reconstruirse a mano del lado de la UI.
/// </summary>
public sealed record RecomendacionPermiso(string PermisoRecomendado, string Mensaje);

/// <summary>
/// Dependencias entre permisos configurables (refactor panel de permisos, paso 1 de 7).
/// Hoy nada valida que un conjunto de permisos sea coherente: lo único que lo impedía eran
/// tres checkboxes compuestos del panel (efecto lateral en el ViewModel), que un paso
/// posterior de este mismo refactor va a eliminar. Esta clase es la validación de verdad,
/// del lado servidor, que protege también a quien pegue un conjunto de permisos directo por
/// API cruda. Listas/diccionarios literales explícitos, sin reflection — misma convención que
/// <see cref="Permisos.Todos"/> (ver Permisos.cs, "sin reflection"): la reflexión por
/// convención de nombres es justamente el mecanismo que ya falló antes en este módulo.
/// </summary>
public static class PermisoDependencias
{
    /// <summary>
    /// Dependencias DURAS: si el permiso clave está concedido, el permiso valor es
    /// obligatorio — <c>UsuarioService.GuardarPermisosAsync</c> rechaza con
    /// <c>ReglaDeNegocioException</c> si falta (paso 2 de este refactor). Cada entrada acá
    /// significa que, sin el permiso requerido, el permiso concedido queda 100% inalcanzable
    /// por UI: no es una preferencia, es una combinación inservible.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Requisitos = new Dictionary<string, string>
    {
        // El sidebar gatea Gastos/Ingresos/LibroCaja/ControlPOA/Calendario detrás de
        // PuedeVerFinanzas (ShellMainView.axaml): sin VerFinanzas, no hay pantalla desde la
        // que registrar un gasto, un pago o un ingreso.
        [Permisos.RegistrarGastos]   = Permisos.VerFinanzas,
        [Permisos.RegistrarPagos]    = Permisos.VerFinanzas,
        [Permisos.RegistrarIngresos] = Permisos.VerFinanzas,

        // El botón "Recalcular stock" vive dentro del Historial de movimientos
        // (MovimientoHistorialView.axaml:80-81), gateado por PuedeRegistrarMovimientos
        // (ShellMainView.axaml:129): sin RegistrarMovimientos, no hay forma de llegar al botón.
        [Permisos.RecalcularStock] = Permisos.RegistrarMovimientos,
    };

    /// <summary>
    /// Dependencia BLANDA: se avisa pero se permite guardar igual. A diferencia de
    /// <see cref="Requisitos"/>, esta combinación deja un rol útil y real: un Operador con
    /// RegistrarMovimientos y sin GestionarProductos conserva la consulta completa del
    /// historial de movimientos (filtra, ve todo) y puede recalcular stock — el campo de
    /// producto de Recalcular es un NumericUpDown tipeado a mano, no el combo de productos.
    /// Bloquear esta combinación mataría ese rol; por eso el panel la va a mostrar como aviso,
    /// no como rechazo (decisión ya tomada, no se re-litiga acá).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, RecomendacionPermiso> Recomendados =
        new Dictionary<string, RecomendacionPermiso>
        {
            [Permisos.RegistrarMovimientos] = new RecomendacionPermiso(
                Permisos.GestionarProductos,
                "Sin permiso de productos solo va a poder consultar el historial de movimientos, " +
                "no registrar movimientos nuevos ni ingresos por factura."),
        };
}
