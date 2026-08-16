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
/// Panel de permisos de la columna derecha de UsuariosAdminView (spec 2026-08-10, Task 12;
/// aplanado Tasks 3/4/6, spec 2026-08-15). 12 checkboxes independientes, uno por permiso
/// configurable, generados desde CatalogoPermisosPanel -- ya no hay checkboxes compuestos ni
/// efectos laterales entre ítems: el Admin ve y concede EXACTAMENTE lo que tilda. La
/// protección contra combinaciones inválidas (ej. RegistrarGastos sin RegistrarMovimientos) la
/// da UsuarioService.GuardarPermisosAsync, validando PermisoDependencias.Requisitos.
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

    /// <summary>Los 12 checkboxes agrupados por sección, construidos UNA vez a partir de
    /// CatalogoPermisosPanel.Entradas -- en el orden de declaración del catálogo, nunca
    /// alfabético (Documentos va último en el catálogo aunque "Documentos" ordenaría 2do).</summary>
    public IReadOnlyList<GrupoPermisos> Grupos { get; }

    public bool EsAdminSeleccionado => _padre?.EsAdminSeleccionado ?? false;

    /// <summary>Crítico 2, capa b (review Task 13): estado explícito de "no se pudieron cargar
    /// los permisos" — un panel simplemente destildado no le avisa nada al Admin, y si aprieta
    /// Guardar le saca TODOS los permisos al usuario seleccionado. Mismo patrón que
    /// FuenteFinanciamientoFormViewModel/RubroGastoFormViewModel/etc. usan en este repo:
    /// MensajeError + [NotifyCanExecuteChangedFor(GuardarCommand)] + PuedeGuardar().</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string? _mensajeError;

    public PanelPermisosViewModel(IUsuarioService usuarios, IConfirmacionService confirmacion)
    {
        _usuarios = usuarios;
        _confirmacion = confirmacion;

        Grupos = CatalogoPermisosPanel.Entradas
            .GroupBy(e => e.Grupo)
            .Select(g => new GrupoPermisos(
                g.Key,
                g.Select(e => new ItemPermiso(e.Permiso, e.Etiqueta)).ToList()))
            .ToList();

        // Paso 5 del refactor: el aviso de una dependencia BLANDA (PermisoDependencias.
        // Recomendados) depende del estado de OTRO ítem -- destildar GestionarProductos tiene
        // que reaparecer el aviso de RegistrarMovimientos, no el propio. Por eso se escucha el
        // PropertyChanged de TODOS los ítems (no solo "el que cambió" desde afuera) y se
        // recalculan TODAS las advertencias juntas cada vez: son 12 ítems, recorrerlos entero
        // es más simple y más difícil de romper que mantener un grafo de dependencias inversas
        // a mano. Suscripción única acá porque los ítems nunca cambian de identidad (Grupos se
        // arma una sola vez, ver comentario de la propiedad).
        foreach (var item in Grupos.SelectMany(g => g.Items))
            item.PropertyChanged += AlCambiarSeleccionDeItem;
    }

    private void AlCambiarSeleccionDeItem(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ItemPermiso.Seleccionado)) return;
        RecalcularAdvertencias();
    }

    /// <summary>No bloquea nada -- la única dependencia que puede impedir el guardado es la
    /// DURA (PermisoDependencias.Requisitos), validada del lado servidor en
    /// UsuarioService.GuardarPermisosAsync (ya mergeado, no se toca acá).</summary>
    private void RecalcularAdvertencias()
    {
        var seleccionados = new HashSet<string>(
            Grupos.SelectMany(g => g.Items).Where(i => i.Seleccionado).Select(i => i.Clave));

        foreach (var item in Grupos.SelectMany(g => g.Items))
        {
            item.Advertencia =
                item.Seleccionado
                && PermisoDependencias.Recomendados.TryGetValue(item.Clave, out var recomendacion)
                && !seleccionados.Contains(recomendacion.PermisoRecomendado)
                    ? recomendacion.Mensaje
                    : null;
        }
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
            var seleccionados = new HashSet<string>(permisos);
            foreach (var item in Grupos.SelectMany(g => g.Items))
                item.Seleccionado = seleccionados.Contains(item.Clave);
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
        foreach (var item in Grupos.SelectMany(g => g.Items))
            item.Seleccionado = false;
    }

    /// <summary>Crítico 2, capa b: gatea GuardarCommand mientras MensajeError esté seteado
    /// (última carga fallida) — evita que el Admin persista, sin querer, los permisos que
    /// quedaron en el modelo desde antes de que el fetch fallara.</summary>
    private bool PuedeGuardar() => MensajeError is null;

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        if (_padre?.UsuarioSeleccionado is null) return;

        var seleccionados = Grupos.SelectMany(g => g.Items)
            .Where(i => i.Seleccionado)
            .Select(i => i.Clave)
            .ToList();

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
