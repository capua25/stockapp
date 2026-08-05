using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels;

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
}

/// <summary>Sesion fake con rol configurable -- es exactamente el rol el que decide, en
/// TareaFila.PuedeCancelar y TareaFormViewModel.EsAdmin, que controles debe exponer la VISTA.</summary>
internal sealed class TareaSessionFake : ICurrentSession
{
    public TareaSessionFake(RolUsuario rol) => RolActual = rol;

    public bool EstaAutenticado => true;
    public StockApp.Application.Auth.UsuarioSesion? UsuarioActual => null;
    public RolUsuario? RolActual { get; }

    public void IniciarSesion(Usuario usuario) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public void CerrarSesion() => throw new NotSupportedException("No usado en este banco de pruebas.");
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

    public void Navegar<TVm>() where TVm : ViewModelBase => UltimoTipoNavegado = typeof(TVm);

    public void Navegar<TVm>(Action<TVm> inicializar) where TVm : ViewModelBase
        => UltimoTipoNavegado = typeof(TVm);
}
