using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Proyectos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Formato;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Proyectos;

/// <summary>Fila de solo lectura de la lista de proyectos: aplana la entidad para la grilla.</summary>
public sealed class ProyectoFila
{
    public Proyecto Proyecto { get; }

    public ProyectoFila(Proyecto proyecto) => Proyecto = proyecto;

    public int Id => Proyecto.Id;
    public string Titulo => Proyecto.Titulo;
    public string PrioridadTexto => Proyecto.Prioridad.ToString();
    public string ZonaNombre => Proyecto.Zona?.Nombre ?? "—";
    public decimal? Presupuesto => Proyecto.Presupuesto;

    /// <summary>Columna PRESUPUESTO: en pesos es-UY, o "—" si el proyecto no tiene presupuesto (null, distinto de 0).</summary>
    public string PresupuestoTexto => Proyecto.Presupuesto is decimal p ? FormatoEsUy.Moneda(p) : "—";

    public DateTime? FechaLimite => Proyecto.FechaLimite;
}

/// <summary>Opción del combo de Estado del listado: el conjunto de estados que deja ver (filtro en el cliente).</summary>
public sealed record OpcionEstadoProyecto(string Nombre, IReadOnlyList<EstadoProyecto> Estados);

/// <summary>
/// Pantalla "Proyectos" (spec 2026-10-30): una sola colección filtrada en el cliente por Estado ("Abiertos" por
/// defecto), igual que <see cref="Tareas.TareaListViewModel"/>. La vista dispara CargarAsync() vía DataContextChanged.
/// Las acciones de estado viven en la ficha, no en la lista.
/// </summary>
public partial class ProyectoListViewModel : ViewModelBase
{
    /// <summary>Texto del estado "Sin permiso" cuando el servidor rechaza la carga (403) o se pierde el permiso en caliente.</summary>
    public const string MensajeSinPermisoProyectos = "No tenés permiso para ver los proyectos.";

    private readonly IProyectoService   _service;
    private readonly ICurrentSession    _session;
    private readonly INavigationService _navigation;
    private readonly ConsultaVigente    _consultas = new();

    public ObservableCollection<ProyectoFila> Proyectos { get; } = new();

    /// <summary>La colección de <see cref="Proyectos"/> con el filtro de Estado aplicado: lo que ve la grilla.</summary>
    public DataGridCollectionView ProyectosView { get; }

    /// <summary>Opciones del combo de Estado, en el orden en que se muestran.</summary>
    public IReadOnlyList<OpcionEstadoProyecto> FiltrosEstado { get; } = new[]
    {
        new OpcionEstadoProyecto("Abiertos", new[] { EstadoProyecto.Pendiente, EstadoProyecto.EnCurso }),
        new OpcionEstadoProyecto("Pendientes", new[] { EstadoProyecto.Pendiente }),
        new OpcionEstadoProyecto("En curso", new[] { EstadoProyecto.EnCurso }),
        new OpcionEstadoProyecto("Terminados", new[] { EstadoProyecto.Terminado }),
        new OpcionEstadoProyecto("Cancelados", new[] { EstadoProyecto.Cancelado }),
        new OpcionEstadoProyecto("Todos", new[] { EstadoProyecto.Pendiente, EstadoProyecto.EnCurso, EstadoProyecto.Terminado, EstadoProyecto.Cancelado }),
    };

    [ObservableProperty] private OpcionEstadoProyecto _filtroEstado;

    /// <summary>La fila sobre la que actúa "Ver". Se limpia si el filtro la oculta.</summary>
    [ObservableProperty] private ProyectoFila? _filaSeleccionada;

    private int _cantidadVisible;

    /// <summary>Filas que deja ver el filtro de Estado (el pie "N proyectos").</summary>
    public int CantidadVisible { get => _cantidadVisible; private set => SetProperty(ref _cantidadVisible, value); }

    /// <summary>Gatea la pantalla: Admin o proyectos.ver. Lo re-lee <see cref="NotificarPermisosActualizados"/>.</summary>
    public bool PuedeVerProyectos =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.VerProyectos);

    /// <summary>Gatea "Nuevo proyecto": Admin o proyectos.gestionar.</summary>
    public bool PuedeGestionarProyectos =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarProyectos);

    public ProyectoListViewModel(IProyectoService service, ICurrentSession session, INavigationService navigation)
    {
        _service    = service;
        _session    = session;
        _navigation = navigation;

        _filtroEstado = FiltrosEstado[0];
        ProyectosView = new DataGridCollectionView(Proyectos) { Filter = EsVisible };
        Proyectos.CollectionChanged += (_, _) => Recalcular();
    }

    /// <summary>Predicado del filtro: la grilla y el pie usan el mismo, así lo que se cuenta es lo que se ve.</summary>
    internal bool EsVisible(object item) =>
        item is ProyectoFila fila && FiltroEstado.Estados.Contains(fila.Proyecto.Estado);

    partial void OnFiltroEstadoChanged(OpcionEstadoProyecto value)
    {
        // La selección oculta se suelta ANTES de refrescar (mismo motivo que TareaListViewModel).
        if (FilaSeleccionada is { } seleccionada && !EsVisible(seleccionada))
            FilaSeleccionada = null;
        ProyectosView.Refresh();
        Recalcular();
    }

    private void Recalcular() => CantidadVisible = Proyectos.Count(f => EsVisible(f));

    /// <summary>La vista muestra "No se pudo conectar con el servidor" con Reintentar dentro de la card.</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin servidor el listado queda vacío: el reintento no puede mostrar las filas de la consulta anterior.</summary>
    protected override void AlPerderConexion() => Proyectos.Clear();

    /// <summary>
    /// Un 403 refrescó los permisos en caliente: además de re-leer los <c>Puede*</c> (base), si ya no hay
    /// <c>proyectos.ver</c> se vacía el listado y la pantalla pasa a "Sin permiso".
    /// </summary>
    public override void NotificarPermisosActualizados()
    {
        if (!PuedeVerProyectos)
        {
            Proyectos.Clear();
            MarcarSinPermiso(MensajeSinPermisoProyectos);
        }
        base.NotificarPermisosActualizados();
    }

    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var proyectos = await _service.ListarAsync();
            if (!turno.EsLaUltima) return;

            Proyectos.Clear();
            foreach (var proyecto in proyectos)
                Proyectos.Add(new ProyectoFila(proyecto));
        }, MensajeSinPermisoProyectos, turno);
    }

    [RelayCommand]
    private void Nuevo() => _navigation.Navegar<ProyectoFormViewModel>(vm => vm.CargarParaCrear());

    [RelayCommand]
    private void VerDetalle(ProyectoFila fila)
        => _navigation.Navegar<ProyectoFormViewModel>(vm => vm.CargarParaVer(fila.Id));
}
