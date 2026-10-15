using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

/// <summary>El debounce compartido: solo la última programación de una ráfaga corre su recarga.</summary>
public class RecargaConDebounceTests
{
    [Fact]
    public async Task Programar_TrasLaEspera_CorreLaRecarga()
    {
        var corridas = 0;
        var recarga = new RecargaConDebounce(() => TimeSpan.Zero);

        await recarga.Programar(() => { corridas++; return Task.CompletedTask; });

        Assert.Equal(1, corridas);
    }

    [Fact]
    public async Task DosProgramacionesSeguidas_SoloCorreLaUltima()
    {
        var corridas = new List<string>();
        var recarga = new RecargaConDebounce(() => TimeSpan.FromMilliseconds(50));

        var primera = recarga.Programar(() => { corridas.Add("primera"); return Task.CompletedTask; });
        var segunda = recarga.Programar(() => { corridas.Add("segunda"); return Task.CompletedTask; });
        await Task.WhenAll(primera, segunda);

        Assert.Equal(new[] { "segunda" }, corridas);
    }

    [Fact]
    public async Task EsperaCancelada_NoCorreLaRecarga_YTerminaSinError()
    {
        var espera = Timeout.InfiniteTimeSpan;
        var recarga = new RecargaConDebounce(() => espera);
        var corrioLaPrimera = false;
        var primera = recarga.Programar(() => { corrioLaPrimera = true; return Task.CompletedTask; });

        espera = TimeSpan.Zero;
        await recarga.Programar(() => Task.CompletedTask);
        await Task.WhenAny(primera, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.True(primera.IsCompletedSuccessfully, "La espera cancelada no terminó o terminó con error.");
        Assert.False(corrioLaPrimera);
    }

    [Fact]
    public async Task EsperaInfinita_NoCorreLaRecarga()
    {
        var espera = Timeout.InfiniteTimeSpan;
        var recarga = new RecargaConDebounce(() => espera);
        var corrio = false;

        var tarea = recarga.Programar(() => { corrio = true; return Task.CompletedTask; });

        Assert.False(tarea.IsCompleted);
        Assert.False(corrio);

        espera = TimeSpan.Zero;                       // libera la espera infinita: no queda un Task.Delay vivo
        await recarga.Programar(() => Task.CompletedTask);
    }

    [Fact]
    public async Task LaEspera_SeLeeEnCadaProgramar()
    {
        var espera = Timeout.InfiniteTimeSpan;
        var recarga = new RecargaConDebounce(() => espera);
        espera = TimeSpan.Zero;                       // como un test que baja EsperaDebounce después de construir el VM
        var corrio = false;

        await recarga.Programar(() => { corrio = true; return Task.CompletedTask; });

        Assert.True(corrio);
    }

    /// <summary>El turno lo pide la carga, no el helper: dos recargas programadas cuya primera respuesta llega tarde no
    /// pisan la segunda.</summary>
    [Fact]
    public async Task ConLaConsultaVigente_LaPrimeraRecargaQueTerminaTarde_NoPisaLaSegunda()
    {
        var consultas = new ConsultaVigente();
        var recarga = new RecargaConDebounce(() => TimeSpan.Zero);
        var vieja = new TaskCompletionSource<string>();
        var nueva = new TaskCompletionSource<string>();
        string? enPantalla = null;

        async Task Cargar(TaskCompletionSource<string> respuesta)
        {
            var turno = consultas.Empezar();
            var resultado = await respuesta.Task;
            if (turno.EsLaUltima)
                enPantalla = resultado;
        }

        var primera = recarga.Programar(() => Cargar(vieja));
        var segunda = recarga.Programar(() => Cargar(nueva));
        nueva.SetResult("nueva");
        await segunda;
        vieja.SetResult("vieja");
        await primera;

        Assert.Equal("nueva", enPantalla);
    }
}
