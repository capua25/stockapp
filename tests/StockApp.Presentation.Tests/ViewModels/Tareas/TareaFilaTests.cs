using System;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Tareas;

/// <summary>
/// Fix (panel de vencimientos, 2026-08-06): la versión anterior de DiasParaVencer comparaba
/// FechaLimite.Date contra DateTime.UtcNow.Date directo. Uruguay es UTC-3: durante las 3 horas
/// antes de la medianoche LOCAL (21:00-23:59), el reloj UTC ya pasó a "mañana" mientras el
/// operador todavía está "hoy" -- una tarea que vence hoy se mostraba vencida un día antes de
/// tiempo. La fecha límite se guarda como una fecha de calendario "etiquetada" UTC (ver
/// TareaFormViewModel.GuardarAsync: FechaLimiteSeleccionada.Value.Date + SpecifyKind Utc, NO una
/// conversión real de zona), así que lo único que hay que corregir es contra qué "hoy" se
/// compara: el día de calendario en la zona LOCAL del operador, no en UTC.
///
/// Todos los tests de esta clase fijan el instante UTC y la zona EXPLÍCITAMENTE (constructor de
/// 4 argumentos + una zona horaria fija UTC-3 armada a mano) en vez de usar DateTime.Now o
/// TimeZoneInfo.Local -- así el resultado no depende de en qué máquina ni en qué zona horaria
/// corre la suite.
/// </summary>
public class TareaFilaTests
{
    /// <summary>
    /// Zona fija UTC-3 (Uruguay), armada a mano en vez de buscarla por Id ("America/Montevideo"
    /// no está garantizado en toda imagen de CI) -- mismo criterio que el pedido del encargo:
    /// "usá instantes absolutos y una zona explícita".
    /// </summary>
    private static readonly TimeZoneInfo ZonaUruguay =
        TimeZoneInfo.CreateCustomTimeZone("UTC-3 (test)", TimeSpan.FromHours(-3), "UTC-3 (test)", "UTC-3 (test)");

    private static Tarea TareaCon(DateTime? fechaLimite, EstadoTarea estado = EstadoTarea.Pendiente) => new()
    {
        Id = 1, Titulo = "x", Estado = estado, Prioridad = PrioridadTarea.Media,
        CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow, FechaLimite = fechaLimite,
    };

    private static DateTime Utc(int anio, int mes, int dia, int hora, int minuto) =>
        new(anio, mes, dia, hora, minuto, 0, DateTimeKind.Utc);

    [Fact]
    public void DiasParaVencer_SinFechaLimite_Devuelve0()
    {
        var fila = new TareaFila(TareaCon(null), RolUsuario.Operador, Utc(2026, 8, 10, 12, 0), ZonaUruguay);
        Assert.Equal(0m, fila.DiasParaVencer);
    }

    [Theory]
    [InlineData(EstadoTarea.Terminada)]
    [InlineData(EstadoTarea.Cancelada)]
    public void DiasParaVencer_EstadoTerminalAunqueEsteVencida_Devuelve0(EstadoTarea estado)
    {
        // Una tarea cerrada no debe resaltarse en rojo aunque su fecha límite ya haya pasado: el
        // resaltado es una señal de "atención pendiente", no un dato histórico.
        var vencidaHaceRato = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var fila = new TareaFila(TareaCon(vencidaHaceRato, estado), RolUsuario.Operador, Utc(2026, 8, 10, 12, 0), ZonaUruguay);

        Assert.Equal(0m, fila.DiasParaVencer);
    }

    [Fact]
    public void DiasParaVencer_FechaFutura_DevuelvePositivo()
    {
        var enCincoDias = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        var fila = new TareaFila(TareaCon(enCincoDias, EstadoTarea.EnCurso), RolUsuario.Operador, Utc(2026, 8, 10, 12, 0), ZonaUruguay);

        Assert.Equal(5m, fila.DiasParaVencer);
    }

    [Fact]
    public void DiasParaVencer_FechaPasada_DevuelveNegativo()
    {
        var haceTresDias = new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc);
        var fila = new TareaFila(TareaCon(haceTresDias, EstadoTarea.Pendiente), RolUsuario.Operador, Utc(2026, 8, 10, 12, 0), ZonaUruguay);

        Assert.Equal(-3m, fila.DiasParaVencer);
    }

    [Fact]
    public void DiasParaVencer_VenceHoy_Devuelve0_NoNegativo()
    {
        // Borde no especificado en el spec original: "vence hoy" da 0, NO un valor negativo. Con
        // el contrato de SignoNegativoBrushConverter (negativo = rojo), esto significa que el día
        // de vencimiento NO se resalta en rojo -- recién se pinta de rojo al día siguiente.
        var hoy = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        var fila = new TareaFila(TareaCon(hoy, EstadoTarea.EnCurso), RolUsuario.Operador, Utc(2026, 8, 10, 12, 0), ZonaUruguay);

        Assert.Equal(0m, fila.DiasParaVencer);
    }

    /// <summary>
    /// TRAMPA 1 del encargo, la que realmente rompe con el cálculo viejo (UtcNow.Date directo):
    /// la tarea vence HOY (10/08 hora local), pero se evalúa a las 22:00 hora local del 10/08 --
    /// en ese instante el reloj UTC YA marca 11/08 01:00 (Uruguay es UTC-3: 22:00 local + 3h =
    /// 01:00 del día siguiente en UTC). El cálculo viejo comparaba FechaLimite.Date (10/08)
    /// contra DateTime.UtcNow.Date (11/08) y daba -1 ("vencida"), un día antes de tiempo. El
    /// cálculo nuevo compara contra el día de calendario LOCAL (10/08 en la zona del operador,
    /// aunque el instante UTC ya sea 11/08) y da 0 ("hoy").
    /// </summary>
    [Fact]
    public void DiasParaVencer_VenceHoy_EvaluadaALas22HorasLocal_Devuelve0_NoVencida()
    {
        var venceHoy = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        // 10/08 22:00 hora Uruguay (UTC-3) = 11/08 01:00 UTC.
        var ahoraUtc = Utc(2026, 8, 11, 1, 0);

        var fila = new TareaFila(TareaCon(venceHoy, EstadoTarea.Pendiente), RolUsuario.Operador, ahoraUtc, ZonaUruguay);

        Assert.Equal(0m, fila.DiasParaVencer);
    }

    /// <summary>
    /// TRAMPA 2 del encargo: la tarea venció AYER (09/08), evaluada a la 01:00 hora local del
    /// 10/08. Nota de verificación (dejame verificar, per protocolo): para una zona UTC-3 el
    /// reloj UTC SIEMPRE pasa de día ANTES que el reloj local (UTC = local + 3h), así que a las
    /// 01:00 hora local el reloj UTC ya está en el mismo día calendario que el local (01:00+3h =
    /// 04:00, mismo 10/08) -- el cálculo viejo también da -1 en este caso puntual, no solo el
    /// nuevo. La ventana de riesgo real para UTC-3 es exclusivamente 21:00-23:59 hora local
    /// (Trampa 1), no la madrugada. Este test queda igual como red de regresión explícita: prueba
    /// que el fix no "sobre-corrige" y sigue dando el valor correcto (-1, nunca "hoy") también en
    /// este extremo del día, con instante y zona fijos como pide el encargo.
    /// </summary>
    [Fact]
    public void DiasParaVencer_VencioAyer_EvaluadaALaUnaDeLaMadrugadaLocal_DevuelveMenos1()
    {
        var vencioAyer = new DateTime(2026, 8, 9, 0, 0, 0, DateTimeKind.Utc);
        // 10/08 01:00 hora Uruguay (UTC-3) = 10/08 04:00 UTC.
        var ahoraUtc = Utc(2026, 8, 10, 4, 0);

        var fila = new TareaFila(TareaCon(vencioAyer, EstadoTarea.Pendiente), RolUsuario.Operador, ahoraUtc, ZonaUruguay);

        Assert.Equal(-1m, fila.DiasParaVencer);
    }

    [Fact]
    public void DiasParaVencer_ConstructorSinInstanteExplicito_UsaElRelojReal()
    {
        // El overload de 2 argumentos (el que ya usan TareaListViewModel y TareaFormViewModel)
        // sigue existiendo y usa DateTime.UtcNow/TimeZoneInfo.Local reales -- no rompe ningún
        // call site existente. Se verifica con una fecha muy lejana para no depender de la hora
        // exacta de ejecución del test.
        var enElFuturoLejano = DateTime.UtcNow.Date.AddYears(5);
        var fila = new TareaFila(TareaCon(enElFuturoLejano, EstadoTarea.Pendiente), RolUsuario.Operador);

        Assert.True(fila.DiasParaVencer > 1000m);
    }
}
