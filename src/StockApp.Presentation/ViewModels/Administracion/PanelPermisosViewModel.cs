using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Administracion;

/// <summary>
/// Panel de permisos de la columna derecha de UsuariosAdminView (spec 2026-08-10, Task 12).
/// 12 checkboxes agrupados por sección, algunos compuestos (tildan 2-3 permisos juntos) y
/// algunos compartidos (bindeados a la MISMA propiedad cuando dos pantallas usan el mismo
/// permiso — tildar uno tilda el otro en el acto, sin lógica extra que lo sincronice).
/// </summary>
public partial class PanelPermisosViewModel : ViewModelBase
{
    private readonly IUsuarioService _usuarios;
    private readonly IConfirmacionService _confirmacion;

    /// <summary>Poblado por Conectar(), llamado una única vez desde el constructor de
    /// UsuariosAdminViewModel — ver Decisión de diseño 2 (ViewLocator exige Views sin
    /// argumentos, así que la composición se resuelve enteramente en el grafo de constructores
    /// de los ViewModels, nunca en el code-behind).</summary>
    private UsuariosAdminViewModel? _padre;

    /// <summary>Expone el fire-and-forget de AlCambiarSeleccion para que los tests lo esperen
    /// de forma determinista, sin Task.Delay (pre-flight, corrección A) — mismo patrón que
    /// ShellViewModel._tareaActualizacion / ProductoListViewModel._tareaDebounce.</summary>
    internal Task _tareaCarga = Task.CompletedTask;

    // ── Catálogo / Stock ───────────────────────────────────────────────────
    // Crítico 1 (review Task 13): las propiedades base que participan de un checkbox compuesto
    // necesitan [NotifyPropertyChangedFor] hacia esa compuesta — si no, Avalonia nunca se entera
    // de que "Productos"/"GastosYFacturas"/"IngresosDeCaja" cambiaron cuando CargarAsync asigna
    // las bases directamente (no pasa por los setters de las compuestas), y el checkbox queda
    // congelado con el estado del usuario anterior. Verificadas las 11: solo estas 5 alimentan
    // el getter de alguna compuesta (Productos, GastosYFacturas o IngresosDeCaja) — el resto son
    // checkboxes standalone bindeados directo a su propia base, que ya se notifican solas.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Productos))]
    private bool _permisoGestionarProductos;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Productos))]
    private bool _permisoRecalcularStock;
    [ObservableProperty] private bool _permisoGestionarTablasMaestras;
    [ObservableProperty] private bool _permisoRegistrarMovimientos;

    // ── Finanzas ───────────────────────────────────────────────────────────
    [ObservableProperty] private bool _permisoVerFinanzas;
    [ObservableProperty] private bool _permisoGestionarMaestrosFinanzas;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GastosYFacturas))]
    private bool _permisoRegistrarGastos;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GastosYFacturas))]
    private bool _permisoRegistrarPagos;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IngresosDeCaja))]
    private bool _permisoRegistrarIngresos;

    // ── Tareas / Reportes ──────────────────────────────────────────────────
    [ObservableProperty] private bool _permisoGestionarTareas;
    [ObservableProperty] private bool _permisoVerReportes;

    // ── Documentos ─────────────────────────────────────────────────────────
    // Bugfix 2026-08-14: faltaba este checkbox — AuthorizationService.PermisosConfigurables
    // ya contaba 12 permisos asignables, pero el panel solo exponía 11. AdministrarDocumentos
    // NO tiene checkbox (es estructural Admin-only, igual que AdministrarTareas).
    [ObservableProperty] private bool _permisoGestionarDocumentos;

    /// <summary>Crítico 2, capa b (review Task 13): estado explícito de "no se pudieron cargar
    /// los permisos" — un panel simplemente destildado no le avisa nada al Admin, y si aprieta
    /// Guardar le saca TODOS los permisos al usuario seleccionado. Mismo patrón que
    /// FuenteFinanciamientoFormViewModel/RubroGastoFormViewModel/etc. usan en este repo:
    /// MensajeError + [NotifyCanExecuteChangedFor(GuardarCommand)] + PuedeGuardar().</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string? _mensajeError;

    /// <summary>Deshabilita el panel entero cuando el usuario seleccionado es Admin (leyenda
    /// "Acceso total" en la View) — proxy de UsuariosAdminViewModel.EsAdminSeleccionado. False
    /// antes de Conectar() (no debería observarse: Conectar corre en el constructor del padre,
    /// antes de que la View pueda bindear nada).</summary>
    public bool EsAdminSeleccionado => _padre?.EsAdminSeleccionado ?? false;

    /// <summary>Checkbox compuesto: Productos → GestionarProductos + RecalcularStock juntos.</summary>
    public bool Productos
    {
        get => PermisoGestionarProductos && PermisoRecalcularStock;
        set
        {
            PermisoGestionarProductos = value;
            PermisoRecalcularStock = value;
            OnPropertyChanged(nameof(Productos));
        }
    }

    /// <summary>Checkbox compuesto: Gastos y facturas → VerFinanzas + RegistrarGastos +
    /// RegistrarPagos. Tildarlo enciende también VerFinanzas (efecto visible, spec); destildarlo
    /// NO apaga VerFinanzas (Libro caja/Control POA/Calendario pueden seguir necesitándolo).</summary>
    public bool GastosYFacturas
    {
        get => PermisoRegistrarGastos && PermisoRegistrarPagos;
        set
        {
            PermisoRegistrarGastos = value;
            PermisoRegistrarPagos = value;
            if (value) PermisoVerFinanzas = true;
            OnPropertyChanged(nameof(GastosYFacturas));
        }
    }

    /// <summary>Checkbox compuesto: Ingresos de caja → VerFinanzas + RegistrarIngresos.</summary>
    public bool IngresosDeCaja
    {
        get => PermisoRegistrarIngresos;
        set
        {
            PermisoRegistrarIngresos = value;
            if (value) PermisoVerFinanzas = true;
            OnPropertyChanged(nameof(IngresosDeCaja));
        }
    }

    public PanelPermisosViewModel(IUsuarioService usuarios, IConfirmacionService confirmacion)
    {
        _usuarios = usuarios;
        _confirmacion = confirmacion;
    }

    /// <summary>Conecta este panel con el UsuariosAdminViewModel que lo hostea. Llamado UNA
    /// VEZ desde el constructor de UsuariosAdminViewModel (Step 5) — nunca desde DI directa:
    /// PanelPermisosViewModel no puede recibir a UsuariosAdminViewModel en su propio
    /// constructor sin crear una dependencia circular en el grafo de DI (ver Decisión de
    /// diseño 2).</summary>
    public void Conectar(UsuariosAdminViewModel padre)
    {
        _padre = padre;
        _padre.PropertyChanged += AlCambiarSeleccion;
    }

    private void AlCambiarSeleccion(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UsuariosAdminViewModel.UsuarioSeleccionado)) return;

        OnPropertyChanged(nameof(EsAdminSeleccionado));
        // Mejor esfuerzo (pre-flight, corrección B): sin esto, una falla de
        // ObtenerPermisosAsync (ej. ServidorNoDisponibleException) quedaba como excepción no
        // observada. El Task envolvente (nunca lanza) se guarda en _tareaCarga para que los
        // tests lo esperen de forma determinista (corrección A).
        _tareaCarga = RefrescoPermisos.DispararBestEffortAsync(CargarAsync, nameof(PanelPermisosViewModel));
    }

    public async Task CargarAsync()
    {
        MensajeError = null;

        if (_padre?.UsuarioSeleccionado is null || _padre.EsAdminSeleccionado)
        {
            LimpiarTodo();
            return;
        }

        // Crítico 2, capa a (review Task 13): limpiar ANTES del await, no después del éxito.
        // Así el panel nunca muestra (ni puede guardar) los permisos del usuario anterior
        // mientras el fetch del nuevo está en vuelo o si termina fallando — sin esto, elegir al
        // Operador B dejaba en pantalla los checkboxes tildados del Operador A.
        LimpiarTodo();

        try
        {
            var permisos = await _usuarios.ObtenerPermisosAsync(_padre.UsuarioSeleccionado.Id);
            PermisoGestionarProductos       = permisos.Contains(Permisos.GestionarProductos);
            PermisoRecalcularStock          = permisos.Contains(Permisos.RecalcularStock);
            PermisoGestionarTablasMaestras  = permisos.Contains(Permisos.GestionarTablasMaestras);
            PermisoRegistrarMovimientos     = permisos.Contains(Permisos.RegistrarMovimientos);
            PermisoVerFinanzas              = permisos.Contains(Permisos.VerFinanzas);
            PermisoGestionarMaestrosFinanzas = permisos.Contains(Permisos.GestionarMaestrosFinanzas);
            PermisoRegistrarGastos          = permisos.Contains(Permisos.RegistrarGastos);
            PermisoRegistrarPagos           = permisos.Contains(Permisos.RegistrarPagos);
            PermisoRegistrarIngresos        = permisos.Contains(Permisos.RegistrarIngresos);
            PermisoGestionarTareas          = permisos.Contains(Permisos.GestionarTareas);
            PermisoVerReportes              = permisos.Contains(Permisos.VerReportes);
            PermisoGestionarDocumentos      = permisos.Contains(Permisos.GestionarDocumentos);
        }
        catch (Exception)
        {
            // Crítico 2, capa b: el panel ya quedó limpio (capa a), pero eso solo no alcanza —
            // MensajeError bloquea GuardarCommand vía PuedeGuardar() hasta que una carga
            // posterior tenga éxito. Se relanza para que RefrescoPermisos.DispararBestEffortAsync
            // (que sigue envolviendo esta llamada desde AlCambiarSeleccion) la registre en
            // crash.log — la UI se entera por MensajeError (mostrado en la View, review Task 13
            // Round 2), el log queda para diagnóstico. Mensaje accionable, no solo informativo
            // (mismo criterio que el bloqueo del auto-cambio de contraseña en
            // UsuariosAdminViewModel.CambiarContrasenaAsync): explica qué pasó y qué hacer.
            MensajeError = "No se pudieron cargar los permisos de este usuario. Volvé a seleccionarlo en la " +
                "lista para reintentar la carga — Guardar va a seguir deshabilitado hasta que se cargue bien.";
            throw;
        }
    }

    private void LimpiarTodo()
    {
        PermisoGestionarProductos = false;
        PermisoRecalcularStock = false;
        PermisoGestionarTablasMaestras = false;
        PermisoRegistrarMovimientos = false;
        PermisoVerFinanzas = false;
        PermisoGestionarMaestrosFinanzas = false;
        PermisoRegistrarGastos = false;
        PermisoRegistrarPagos = false;
        PermisoRegistrarIngresos = false;
        PermisoGestionarTareas = false;
        PermisoVerReportes = false;
        PermisoGestionarDocumentos = false;
    }

    /// <summary>Crítico 2, capa b: gatea GuardarCommand mientras MensajeError esté seteado
    /// (última carga fallida) — evita que el Admin persista, sin querer, los permisos que
    /// quedaron en el modelo desde antes de que el fetch fallara.</summary>
    private bool PuedeGuardar() => MensajeError is null;

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        if (_padre?.UsuarioSeleccionado is null) return;

        var seleccionados = new List<string>();
        if (PermisoGestionarProductos) seleccionados.Add(Permisos.GestionarProductos);
        if (PermisoRecalcularStock) seleccionados.Add(Permisos.RecalcularStock);
        if (PermisoGestionarTablasMaestras) seleccionados.Add(Permisos.GestionarTablasMaestras);
        if (PermisoRegistrarMovimientos) seleccionados.Add(Permisos.RegistrarMovimientos);
        if (PermisoVerFinanzas) seleccionados.Add(Permisos.VerFinanzas);
        if (PermisoGestionarMaestrosFinanzas) seleccionados.Add(Permisos.GestionarMaestrosFinanzas);
        if (PermisoRegistrarGastos) seleccionados.Add(Permisos.RegistrarGastos);
        if (PermisoRegistrarPagos) seleccionados.Add(Permisos.RegistrarPagos);
        if (PermisoRegistrarIngresos) seleccionados.Add(Permisos.RegistrarIngresos);
        if (PermisoGestionarTareas) seleccionados.Add(Permisos.GestionarTareas);
        if (PermisoVerReportes) seleccionados.Add(Permisos.VerReportes);
        if (PermisoGestionarDocumentos) seleccionados.Add(Permisos.GestionarDocumentos);

        // Feedback faltante (reporte de uso real 2026-08-14): guardar no mostraba NINGÚN
        // mensaje, ni de éxito ni de error — quedaba indistinguible de una falla silenciosa.
        // Mismo mecanismo que usan CambiarRolAsync/CambiarContrasenaAsync en
        // UsuariosAdminViewModel (el padre de este panel): IConfirmacionService.InformarAsync
        // para ambos casos, en vez de un MensajeExito propio — es el único patrón de
        // confirmación puntual que existe en toda la app.
        try
        {
            await _usuarios.GuardarPermisosAsync(_padre.UsuarioSeleccionado.Id, seleccionados);
            await _confirmacion.InformarAsync("Permisos guardados.");
        }
        // Mismo criterio que BajaAsync/CambiarRolAsync: un 403 ya dispara el aviso central en
        // App.axaml.cs, mostrar acá ex.Message duplicaría el aviso para el mismo evento.
        catch (UnauthorizedAccessException)
        {
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or ArgumentException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
}
