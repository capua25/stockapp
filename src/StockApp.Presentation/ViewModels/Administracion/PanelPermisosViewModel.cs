using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Administracion;

/// <summary>
/// Panel de permisos de la columna derecha de UsuariosAdminView (spec 2026-08-10, Task 12).
/// 11 checkboxes agrupados por sección, algunos compuestos (tildan 2-3 permisos juntos) y
/// algunos compartidos (bindeados a la MISMA propiedad cuando dos pantallas usan el mismo
/// permiso — tildar uno tilda el otro en el acto, sin lógica extra que lo sincronice).
/// </summary>
public partial class PanelPermisosViewModel : ViewModelBase
{
    private readonly IUsuarioService _usuarios;

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
    [ObservableProperty] private bool _permisoGestionarProductos;
    [ObservableProperty] private bool _permisoRecalcularStock;
    [ObservableProperty] private bool _permisoGestionarTablasMaestras;
    [ObservableProperty] private bool _permisoRegistrarMovimientos;

    // ── Finanzas ───────────────────────────────────────────────────────────
    [ObservableProperty] private bool _permisoVerFinanzas;
    [ObservableProperty] private bool _permisoGestionarMaestrosFinanzas;
    [ObservableProperty] private bool _permisoRegistrarGastos;
    [ObservableProperty] private bool _permisoRegistrarPagos;
    [ObservableProperty] private bool _permisoRegistrarIngresos;

    // ── Tareas / Reportes ──────────────────────────────────────────────────
    [ObservableProperty] private bool _permisoGestionarTareas;
    [ObservableProperty] private bool _permisoVerReportes;

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

    public PanelPermisosViewModel(IUsuarioService usuarios)
    {
        _usuarios = usuarios;
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
        if (_padre?.UsuarioSeleccionado is null || _padre.EsAdminSeleccionado)
        {
            LimpiarTodo();
            return;
        }

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
    }

    [RelayCommand]
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

        await _usuarios.GuardarPermisosAsync(_padre.UsuarioSeleccionado.Id, seleccionados);
    }
}
