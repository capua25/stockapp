using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class TareaTests
{
    private static Tarea NuevaTarea(EstadoTarea estado = EstadoTarea.Pendiente) => new()
    {
        Titulo = "Reparar bache en calle Rivera",
        CreadaPorUsuarioId = 1,
        FechaCreacion = DateTime.UtcNow,
        Estado = estado,
    };

    // ── Transiciones válidas (decisión 5 del spec) ────────────────────────────

    [Fact]
    public void CambiarEstado_PendienteAEnCurso_Permitido()
    {
        var tarea = NuevaTarea(EstadoTarea.Pendiente);
        tarea.CambiarEstado(EstadoTarea.EnCurso);
        Assert.Equal(EstadoTarea.EnCurso, tarea.Estado);
    }

    [Fact]
    public void CambiarEstado_EnCursoAPendiente_Permitido()
    {
        var tarea = NuevaTarea(EstadoTarea.EnCurso);
        tarea.CambiarEstado(EstadoTarea.Pendiente);
        Assert.Equal(EstadoTarea.Pendiente, tarea.Estado);
    }

    [Fact]
    public void CambiarEstado_EnCursoATerminada_Permitido()
    {
        var tarea = NuevaTarea(EstadoTarea.EnCurso);
        tarea.CambiarEstado(EstadoTarea.Terminada);
        Assert.Equal(EstadoTarea.Terminada, tarea.Estado);
    }

    [Fact]
    public void CambiarEstado_PendienteACancelada_Permitido()
    {
        var tarea = NuevaTarea(EstadoTarea.Pendiente);
        tarea.CambiarEstado(EstadoTarea.Cancelada);
        Assert.Equal(EstadoTarea.Cancelada, tarea.Estado);
    }

    [Fact]
    public void CambiarEstado_EnCursoACancelada_Permitido()
    {
        var tarea = NuevaTarea(EstadoTarea.EnCurso);
        tarea.CambiarEstado(EstadoTarea.Cancelada);
        Assert.Equal(EstadoTarea.Cancelada, tarea.Estado);
    }

    // ── Transiciones inválidas ────────────────────────────────────────────────

    [Fact]
    public void CambiarEstado_PendienteATerminada_LanzaReglaDeNegocio()
    {
        var tarea = NuevaTarea(EstadoTarea.Pendiente);
        Assert.Throws<ReglaDeNegocioException>(() => tarea.CambiarEstado(EstadoTarea.Terminada));
        Assert.Equal(EstadoTarea.Pendiente, tarea.Estado);
    }

    [Fact]
    public void CambiarEstado_PendienteAPendiente_LanzaReglaDeNegocio()
    {
        var tarea = NuevaTarea(EstadoTarea.Pendiente);
        Assert.Throws<ReglaDeNegocioException>(() => tarea.CambiarEstado(EstadoTarea.Pendiente));
        Assert.Equal(EstadoTarea.Pendiente, tarea.Estado);
    }

    [Fact]
    public void CambiarEstado_EnCursoAEnCurso_LanzaReglaDeNegocio()
    {
        var tarea = NuevaTarea(EstadoTarea.EnCurso);
        Assert.Throws<ReglaDeNegocioException>(() => tarea.CambiarEstado(EstadoTarea.EnCurso));
        Assert.Equal(EstadoTarea.EnCurso, tarea.Estado);
    }

    // ── Terminalidad: Terminada y Cancelada no tienen salida ──────────────────

    [Theory]
    [InlineData(EstadoTarea.Pendiente)]
    [InlineData(EstadoTarea.EnCurso)]
    [InlineData(EstadoTarea.Terminada)]
    [InlineData(EstadoTarea.Cancelada)]
    public void CambiarEstado_DesdeTerminada_SiempreLanzaReglaDeNegocio(EstadoTarea destino)
    {
        var tarea = NuevaTarea(EstadoTarea.Terminada);
        Assert.Throws<ReglaDeNegocioException>(() => tarea.CambiarEstado(destino));
        Assert.Equal(EstadoTarea.Terminada, tarea.Estado);
    }

    [Theory]
    [InlineData(EstadoTarea.Pendiente)]
    [InlineData(EstadoTarea.EnCurso)]
    [InlineData(EstadoTarea.Terminada)]
    [InlineData(EstadoTarea.Cancelada)]
    public void CambiarEstado_DesdeCancelada_SiempreLanzaReglaDeNegocio(EstadoTarea destino)
    {
        var tarea = NuevaTarea(EstadoTarea.Cancelada);
        Assert.Throws<ReglaDeNegocioException>(() => tarea.CambiarEstado(destino));
        Assert.Equal(EstadoTarea.Cancelada, tarea.Estado);
    }

    [Fact]
    public void Tarea_Nueva_PrioridadPorDefectoEsMedia()
    {
        var tarea = new Tarea { Titulo = "x", CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow };
        Assert.Equal(PrioridadTarea.Media, tarea.Prioridad);
    }

    // ── CambiarPrioridad (decisión 14 del spec): rechazada en estados terminales ──────────

    [Fact]
    public void CambiarPrioridad_DesdePendiente_Permitido()
    {
        var tarea = NuevaTarea(EstadoTarea.Pendiente);
        tarea.CambiarPrioridad(PrioridadTarea.Alta);
        Assert.Equal(PrioridadTarea.Alta, tarea.Prioridad);
    }

    [Fact]
    public void CambiarPrioridad_DesdeEnCurso_Permitido()
    {
        var tarea = NuevaTarea(EstadoTarea.EnCurso);
        tarea.CambiarPrioridad(PrioridadTarea.Alta);
        Assert.Equal(PrioridadTarea.Alta, tarea.Prioridad);
    }

    [Fact]
    public void CambiarPrioridad_DesdeTerminada_LanzaReglaDeNegocioYNoCambiaLaPrioridad()
    {
        var tarea = NuevaTarea(EstadoTarea.Terminada);
        var prioridadOriginal = tarea.Prioridad;

        Assert.Throws<ReglaDeNegocioException>(() => tarea.CambiarPrioridad(PrioridadTarea.Alta));

        Assert.Equal(prioridadOriginal, tarea.Prioridad);
    }

    [Fact]
    public void CambiarPrioridad_DesdeCancelada_LanzaReglaDeNegocioYNoCambiaLaPrioridad()
    {
        var tarea = NuevaTarea(EstadoTarea.Cancelada);
        var prioridadOriginal = tarea.Prioridad;

        Assert.Throws<ReglaDeNegocioException>(() => tarea.CambiarPrioridad(PrioridadTarea.Alta));

        Assert.Equal(prioridadOriginal, tarea.Prioridad);
    }

    // ── PuedeTransicionarA (fix review final, Minor): misma tabla que CambiarEstado, ─────
    // pero de solo lectura -- es la fuente única que consulta TareaFila en Presentation
    // en vez de recodificar las transiciones a mano.

    [Theory]
    [InlineData(EstadoTarea.Pendiente, EstadoTarea.EnCurso, true)]
    [InlineData(EstadoTarea.Pendiente, EstadoTarea.Cancelada, true)]
    [InlineData(EstadoTarea.Pendiente, EstadoTarea.Terminada, false)]
    [InlineData(EstadoTarea.Pendiente, EstadoTarea.Pendiente, false)]
    [InlineData(EstadoTarea.EnCurso, EstadoTarea.Pendiente, true)]
    [InlineData(EstadoTarea.EnCurso, EstadoTarea.Terminada, true)]
    [InlineData(EstadoTarea.EnCurso, EstadoTarea.Cancelada, true)]
    [InlineData(EstadoTarea.EnCurso, EstadoTarea.EnCurso, false)]
    [InlineData(EstadoTarea.Terminada, EstadoTarea.Pendiente, false)]
    [InlineData(EstadoTarea.Terminada, EstadoTarea.EnCurso, false)]
    [InlineData(EstadoTarea.Terminada, EstadoTarea.Cancelada, false)]
    [InlineData(EstadoTarea.Terminada, EstadoTarea.Terminada, false)]
    [InlineData(EstadoTarea.Cancelada, EstadoTarea.Pendiente, false)]
    [InlineData(EstadoTarea.Cancelada, EstadoTarea.EnCurso, false)]
    [InlineData(EstadoTarea.Cancelada, EstadoTarea.Terminada, false)]
    [InlineData(EstadoTarea.Cancelada, EstadoTarea.Cancelada, false)]
    public void PuedeTransicionarA_ReflejaExactamenteLaMismaTablaQueCambiarEstado(
        EstadoTarea origen, EstadoTarea destino, bool esperado)
    {
        var tarea = NuevaTarea(origen);

        Assert.Equal(esperado, tarea.PuedeTransicionarA(destino));
        // De solo lectura: consultar no debe mutar el estado.
        Assert.Equal(origen, tarea.Estado);
    }
}
