using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Tareas;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Fakes minimos de las dependencias de TareaListViewModel/TareaFormViewModel, mismo criterio que
/// NuevaImportacionFakes.cs y MovimientoRegistroFakes.cs (este proyecto no referencia Moq). A
/// diferencia de los fakes que solo devuelven datos fijos, TareaServiceFake MUTA las Tarea reales
/// (via Tarea.CambiarEstado/CambiarPrioridad, las mismas reglas del dominio) para que un test que
/// hace click en "Tomar" y despues vuelve a leer la lista vea el estado nuevo de verdad -- eso es
/// lo que permite verificar un RECORRIDO (click -> reload -> la fila se movio de seccion), no solo
/// que el comando se llamo.
/// </summary>
internal sealed class TareaServiceFake : ITareaService
{
    private readonly List<Tarea> _tareas;

    public TareaServiceFake(List<Tarea>? tareas = null) => _tareas = tareas ?? new List<Tarea>();

    public List<Tarea> TareasCreadas { get; } = new();
    public List<(int Id, string Texto)> NotasAgregadas { get; } = new();
    public List<(int Id, PrioridadTarea Prioridad)> CambiosDePrioridad { get; } = new();
    public List<(int Id, DatosClasificacionTarea Datos)> Reclasificaciones { get; } = new();
    public int LlamadasCancelar { get; private set; }

    public Task<int> CrearAsync(Tarea tarea)
    {
        tarea.Id = _tareas.Count + 1;
        _tareas.Add(tarea);
        TareasCreadas.Add(tarea);
        return Task.FromResult(tarea.Id);
    }

    public Task<IReadOnlyList<Tarea>> ListarAsync() =>
        Task.FromResult<IReadOnlyList<Tarea>>(_tareas.ToList());

    public Task TomarAsync(int id)
    {
        var tarea = _tareas.First(t => t.Id == id);
        tarea.CambiarEstado(EstadoTarea.EnCurso);
        tarea.TomadaPor = new Usuario { NombreUsuario = "operador1" };
        return Task.CompletedTask;
    }

    public Task SoltarAsync(int id)
    {
        var tarea = _tareas.First(t => t.Id == id);
        tarea.CambiarEstado(EstadoTarea.Pendiente);
        tarea.TomadaPor = null;
        return Task.CompletedTask;
    }

    public Task TerminarAsync(int id)
    {
        var tarea = _tareas.First(t => t.Id == id);
        tarea.CambiarEstado(EstadoTarea.Terminada);
        return Task.CompletedTask;
    }

    public Task CancelarAsync(int id)
    {
        var tarea = _tareas.First(t => t.Id == id);
        tarea.CambiarEstado(EstadoTarea.Cancelada);
        LlamadasCancelar++;
        return Task.CompletedTask;
    }

    public Task CambiarPrioridadAsync(int id, PrioridadTarea prioridad)
    {
        var tarea = _tareas.First(t => t.Id == id);
        tarea.CambiarPrioridad(prioridad);
        CambiosDePrioridad.Add((id, prioridad));
        return Task.CompletedTask;
    }

    public Task AgregarNotaAsync(int id, string texto)
    {
        var tarea = _tareas.First(t => t.Id == id);
        tarea.Notas.Add(new NotaTarea { TareaId = id, Texto = texto, Fecha = DateTime.UtcNow });
        NotasAgregadas.Add((id, texto));
        return Task.CompletedTask;
    }

    public Task<Tarea?> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_tareas.FirstOrDefault(t => t.Id == id));

    public Task ReclasificarAsync(int tareaId, DatosClasificacionTarea datos)
    {
        var tarea = _tareas.First(t => t.Id == tareaId);
        tarea.ZonaId = datos.ZonaId;
        tarea.DimensionTematicaId = datos.DimensionTematicaId;
        tarea.OrganismoResponsableId = datos.OrganismoResponsableId;
        tarea.OrigenFinanciamientoId = datos.OrigenFinanciamientoId;
        tarea.DocumentoAdministrativoId = datos.DocumentoAdministrativoId;
        Reclasificaciones.Add((tareaId, datos));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Tarea>> ListarPorDocumentoAsync(int documentoId) =>
        Task.FromResult<IReadOnlyList<Tarea>>(_tareas.Where(t => t.DocumentoAdministrativoId == documentoId).ToList());
}

/// <summary>
/// A diferencia de NavigationServiceFake (MovimientoRegistroFakes.cs, que ignora toda navegacion),
/// este fake GRABA a que ViewModel se navego -- necesario para verificar con un click real que
/// "Nueva tarea"/"Ver"/"Volver"/"Guardar" efectivamente disparan la navegacion esperada, no solo
/// que el boton existe.
/// </summary>
internal sealed class NavigationRecorderFake : INavigationService
{
    public ViewModelBase? Actual => null;
    public event Action? Cambiado { add { } remove { } }

    public Type? UltimoTipoNavegado { get; private set; }

    /// <summary>
    /// Captura el inicializador cuando TVm es TareaFormViewModel (panel de vencimientos de
    /// Inicio, 2026-08-06): permite ejecutarlo contra un TareaFormViewModel real en el test y
    /// confirmar que la fila CORRECTA navegó (no solo que se navegó a algún tipo correcto).
    /// </summary>
    public Action<TareaFormViewModel>? UltimoInicializadorTareaForm { get; private set; }

    public void Navegar<TVm>() where TVm : ViewModelBase => UltimoTipoNavegado = typeof(TVm);

    public void Navegar<TVm>(Action<TVm> inicializar) where TVm : ViewModelBase
    {
        UltimoTipoNavegado = typeof(TVm);
        if (inicializar is Action<TareaFormViewModel> accionTareaForm)
            UltimoInicializadorTareaForm = accionTareaForm;
    }
}

/// <summary>Solo ListarActivasAsync tiene comportamiento real (lo que consume
/// ClasificacionTareaPanelViewModel) — el resto de ICategoriaService no lo ejercita ningún
/// test de este plan, mismo criterio que AuthServiceFake.LoginAsync/LogoutAsync.</summary>
internal sealed class ZonaServiceFake : IZonaService
{
    private readonly List<Zona> _activas;
    public ZonaServiceFake(List<Zona>? activas = null) => _activas = activas ?? new List<Zona>();
    public Task<IReadOnlyList<Zona>> ListarActivasAsync() => Task.FromResult<IReadOnlyList<Zona>>(_activas);
    public Task<int> AltaAsync(Zona zona) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(Zona zona) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<Zona>> ListarTodasAsync() => throw new NotSupportedException("No usado en este banco de pruebas.");
}

internal sealed class DimensionTematicaServiceFake : IDimensionTematicaService
{
    private readonly List<DimensionTematica> _activas;
    public DimensionTematicaServiceFake(List<DimensionTematica>? activas = null) => _activas = activas ?? new List<DimensionTematica>();
    public Task<IReadOnlyList<DimensionTematica>> ListarActivasAsync() => Task.FromResult<IReadOnlyList<DimensionTematica>>(_activas);
    public Task<int> AltaAsync(DimensionTematica d) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(DimensionTematica d) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<DimensionTematica>> ListarTodasAsync() => throw new NotSupportedException("No usado en este banco de pruebas.");
}

internal sealed class OrganismoResponsableServiceFake : IOrganismoResponsableService
{
    private readonly List<OrganismoResponsable> _activas;
    public OrganismoResponsableServiceFake(List<OrganismoResponsable>? activas = null) => _activas = activas ?? new List<OrganismoResponsable>();
    public Task<IReadOnlyList<OrganismoResponsable>> ListarActivasAsync() => Task.FromResult<IReadOnlyList<OrganismoResponsable>>(_activas);
    public Task<int> AltaAsync(OrganismoResponsable o) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(OrganismoResponsable o) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<OrganismoResponsable>> ListarTodasAsync() => throw new NotSupportedException("No usado en este banco de pruebas.");
}

internal sealed class OrigenFinanciamientoServiceFake : IOrigenFinanciamientoService
{
    private readonly List<OrigenFinanciamiento> _activas;
    public OrigenFinanciamientoServiceFake(List<OrigenFinanciamiento>? activas = null) => _activas = activas ?? new List<OrigenFinanciamiento>();
    public Task<IReadOnlyList<OrigenFinanciamiento>> ListarActivasAsync() => Task.FromResult<IReadOnlyList<OrigenFinanciamiento>>(_activas);
    public Task<int> AltaAsync(OrigenFinanciamiento o) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(OrigenFinanciamiento o) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<OrigenFinanciamiento>> ListarTodasAsync() => throw new NotSupportedException("No usado en este banco de pruebas.");
}

/// <summary>Fake del diálogo de reclasificación (Task 10): en vez de abrir una Window real,
/// devuelve ResultadoADevolver y graba lo que recibió — mismo criterio que
/// NavigationRecorderFake para verificar SIN levantar el diálogo real.</summary>
internal sealed class ClasificacionDialogServiceFake : IClasificacionTareaDialogService
{
    public DatosClasificacionTarea? ResultadoADevolver { get; set; }
    public DatosClasificacionTarea? UltimoActualRecibido { get; private set; }
    public int Llamadas { get; private set; }

    public Task<DatosClasificacionTarea?> PedirClasificacionAsync(DatosClasificacionTarea actual)
    {
        Llamadas++;
        UltimoActualRecibido = actual;
        return Task.FromResult(ResultadoADevolver);
    }
}
