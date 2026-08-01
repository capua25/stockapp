using System;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Tareas;

/// <summary>
/// Fix (review final, Important): DiasParaVencer no tenía ningún test pese a tener lógica
/// no trivial (fecha nula, estados terminales, resta de fechas, cast a decimal) y un
/// contrato implícito con SignoNegativoBrushConverter (negativo = rojo en la grilla).
/// </summary>
public class TareaFilaTests
{
    private static Tarea TareaCon(DateTime? fechaLimite, EstadoTarea estado = EstadoTarea.Pendiente) => new()
    {
        Id = 1, Titulo = "x", Estado = estado, Prioridad = PrioridadTarea.Media,
        CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow, FechaLimite = fechaLimite,
    };

    [Fact]
    public void DiasParaVencer_SinFechaLimite_Devuelve0()
    {
        var fila = new TareaFila(TareaCon(null), RolUsuario.Operador);
        Assert.Equal(0m, fila.DiasParaVencer);
    }

    [Theory]
    [InlineData(EstadoTarea.Terminada)]
    [InlineData(EstadoTarea.Cancelada)]
    public void DiasParaVencer_EstadoTerminalAunqueEsteVencida_Devuelve0(EstadoTarea estado)
    {
        // Una tarea cerrada no debe resaltarse en rojo aunque su fecha límite ya haya pasado:
        // el resaltado es una señal de "atención pendiente", no un dato histórico.
        var vencidaHaceRato = DateTime.UtcNow.Date.AddDays(-10);
        var fila = new TareaFila(TareaCon(vencidaHaceRato, estado), RolUsuario.Operador);

        Assert.Equal(0m, fila.DiasParaVencer);
    }

    [Fact]
    public void DiasParaVencer_FechaFutura_DevuelvePositivo()
    {
        var enCincoDias = DateTime.UtcNow.Date.AddDays(5);
        var fila = new TareaFila(TareaCon(enCincoDias, EstadoTarea.EnCurso), RolUsuario.Operador);

        Assert.Equal(5m, fila.DiasParaVencer);
    }

    [Fact]
    public void DiasParaVencer_FechaPasada_DevuelveNegativo()
    {
        var haceTresDias = DateTime.UtcNow.Date.AddDays(-3);
        var fila = new TareaFila(TareaCon(haceTresDias, EstadoTarea.Pendiente), RolUsuario.Operador);

        Assert.Equal(-3m, fila.DiasParaVencer);
    }

    [Fact]
    public void DiasParaVencer_VenceHoy_Devuelve0_NoNegativo()
    {
        // Borde no especificado en el spec: "vence hoy" da 0 (FechaLimite.Date == UtcNow.Date),
        // NO un valor negativo. Con el contrato de SignoNegativoBrushConverter (negativo =
        // rojo), esto significa que el día de vencimiento NO se resalta en rojo -- recién se
        // pinta de rojo a partir del día siguiente, cuando la resta pasa a ser negativa.
        var hoy = DateTime.UtcNow.Date;
        var fila = new TareaFila(TareaCon(hoy, EstadoTarea.EnCurso), RolUsuario.Operador);

        Assert.Equal(0m, fila.DiasParaVencer);
    }
}
