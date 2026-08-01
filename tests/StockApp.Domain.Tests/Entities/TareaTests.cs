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
}
