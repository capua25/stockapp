using System;
using System.Collections.Generic;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Tareas;

/// <summary>
/// Agrupación pura para el panel de vencimientos de Inicio (spec 2026-08-06): qué tareas entran
/// (vencidas / próximas a vencer en 3 días), en qué orden, y qué ve cada rol. Todos los tests
/// fijan instante UTC + zona horaria explícitos (mismo criterio que TareaFilaTests) para que el
/// borde de la ventana de 3 días no dependa de la hora real ni de la máquina que corre la suite.
/// </summary>
public class PanelVencimientosTareasTests
{
    private static readonly TimeZoneInfo ZonaUruguay =
        TimeZoneInfo.CreateCustomTimeZone("UTC-3 (test)", TimeSpan.FromHours(-3), "UTC-3 (test)", "UTC-3 (test)");

    // "Ahora" fijo: 10/08/2026 12:00 UTC == 10/08/2026 09:00 hora Uruguay. "Hoy local" = 10/08.
    private static readonly DateTime AhoraUtc = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime HoyLocalMas(int dias) => new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc).AddDays(dias);

    private static Tarea TareaCon(
        int id, DateTime? fechaLimite, EstadoTarea estado = EstadoTarea.Pendiente, int? tomadaPorUsuarioId = null) => new()
    {
        Id = id, Titulo = $"Tarea {id}", Estado = estado, Prioridad = PrioridadTarea.Media,
        CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow, FechaLimite = fechaLimite,
        TomadaPorUsuarioId = tomadaPorUsuarioId,
    };

    private static (IReadOnlyList<TareaFila> Vencidas, IReadOnlyList<TareaFila> Proximas) Agrupar(
        IEnumerable<Tarea> tareas, RolUsuario rol, int usuarioActualId = 1) =>
        PanelVencimientosTareas.Agrupar(tareas, rol, usuarioActualId, AhoraUtc, ZonaUruguay);

    [Fact]
    public void Agrupar_SinTareas_DevuelveAmbasListasVacias()
    {
        var (vencidas, proximas) = Agrupar(new List<Tarea>(), RolUsuario.Admin);

        Assert.Empty(vencidas);
        Assert.Empty(proximas);
    }

    [Fact]
    public void Agrupar_TareaSinFechaLimite_NuncaAparece()
    {
        var tareas = new List<Tarea> { TareaCon(1, null) };
        var (vencidas, proximas) = Agrupar(tareas, RolUsuario.Admin);

        Assert.Empty(vencidas);
        Assert.Empty(proximas);
    }

    [Theory]
    [InlineData(EstadoTarea.Terminada)]
    [InlineData(EstadoTarea.Cancelada)]
    public void Agrupar_TareaEnEstadoTerminal_NuncaAparece_AunqueEsteVencida(EstadoTarea estado)
    {
        var tareas = new List<Tarea> { TareaCon(1, HoyLocalMas(-10), estado) };
        var (vencidas, proximas) = Agrupar(tareas, RolUsuario.Admin);

        Assert.Empty(vencidas);
        Assert.Empty(proximas);
    }

    [Fact]
    public void Agrupar_TareaVencida_EntraEnVencidas()
    {
        var tareas = new List<Tarea> { TareaCon(1, HoyLocalMas(-3), EstadoTarea.Pendiente) };
        var (vencidas, proximas) = Agrupar(tareas, RolUsuario.Admin);

        Assert.Single(vencidas);
        Assert.Empty(proximas);
        Assert.Equal(1, vencidas[0].Id);
    }

    [Fact]
    public void Agrupar_Vencidas_OrdenadasDeMasUrgenteAMenosUrgente()
    {
        // Más urgente = más días vencida = más negativo primero (spec: "-3 días" antes de "-1 día").
        var tareas = new List<Tarea>
        {
            TareaCon(1, HoyLocalMas(-1), EstadoTarea.Pendiente),
            TareaCon(2, HoyLocalMas(-3), EstadoTarea.EnCurso),
        };
        var (vencidas, _) = Agrupar(tareas, RolUsuario.Admin);

        Assert.Equal(new[] { 2, 1 }, new[] { vencidas[0].Id, vencidas[1].Id });
    }

    [Fact]
    public void Agrupar_Proximas_OrdenadasDeMasUrgenteAMenosUrgente()
    {
        // Más urgente = vence antes: hoy (0), luego 2 días, luego 3 días.
        var tareas = new List<Tarea>
        {
            TareaCon(1, HoyLocalMas(3), EstadoTarea.Pendiente),
            TareaCon(2, HoyLocalMas(0), EstadoTarea.EnCurso),
            TareaCon(3, HoyLocalMas(2), EstadoTarea.Pendiente),
        };
        var (_, proximas) = Agrupar(tareas, RolUsuario.Admin);

        Assert.Equal(new[] { 2, 3, 1 }, new[] { proximas[0].Id, proximas[1].Id, proximas[2].Id });
    }

    /// <summary>Borde exacto pedido por el encargo: 3 días entra, 4 días no.</summary>
    [Fact]
    public void Agrupar_VentanaDeProximasAVencer_3DiasEntra_4DiasNoEntra()
    {
        var tareas = new List<Tarea>
        {
            TareaCon(1, HoyLocalMas(3), EstadoTarea.Pendiente),
            TareaCon(2, HoyLocalMas(4), EstadoTarea.Pendiente),
        };
        var (vencidas, proximas) = Agrupar(tareas, RolUsuario.Admin);

        Assert.Empty(vencidas);
        Assert.Single(proximas);
        Assert.Equal(1, proximas[0].Id);
    }

    [Fact]
    public void Agrupar_RolAdmin_VeTodasLasTareas_IncluidasLasDeOtroOperador()
    {
        var tareas = new List<Tarea>
        {
            TareaCon(1, HoyLocalMas(-1), EstadoTarea.EnCurso, tomadaPorUsuarioId: 99),
            TareaCon(2, HoyLocalMas(0), EstadoTarea.Pendiente, tomadaPorUsuarioId: null),
        };
        var (vencidas, proximas) = Agrupar(tareas, RolUsuario.Admin, usuarioActualId: 1);

        Assert.Single(vencidas);
        Assert.Single(proximas);
    }

    [Fact]
    public void Agrupar_RolOperador_NoVeLasTareasTomadasPorOtroOperador()
    {
        var tareas = new List<Tarea>
        {
            TareaCon(1, HoyLocalMas(-1), EstadoTarea.EnCurso, tomadaPorUsuarioId: 99),
        };
        var (vencidas, proximas) = Agrupar(tareas, RolUsuario.Operador, usuarioActualId: 1);

        Assert.Empty(vencidas);
        Assert.Empty(proximas);
    }

    [Fact]
    public void Agrupar_RolOperador_VeLasPropias()
    {
        var tareas = new List<Tarea>
        {
            TareaCon(1, HoyLocalMas(-1), EstadoTarea.EnCurso, tomadaPorUsuarioId: 1),
        };
        var (vencidas, _) = Agrupar(tareas, RolUsuario.Operador, usuarioActualId: 1);

        Assert.Single(vencidas);
    }

    [Fact]
    public void Agrupar_RolOperador_VeLasQueNadieTomo()
    {
        var tareas = new List<Tarea>
        {
            TareaCon(1, HoyLocalMas(0), EstadoTarea.Pendiente, tomadaPorUsuarioId: null),
        };
        var (_, proximas) = Agrupar(tareas, RolUsuario.Operador, usuarioActualId: 1);

        Assert.Single(proximas);
    }
}
