using System.Diagnostics;
using StockApp.Application.Auth;
using Xunit;

namespace StockApp.Application.Tests.Auth;

/// <summary>
/// Cubre el hardening del hardening: el 401 espurio bajo NTP (ver comentario de
/// <see cref="RelojMonotonico"/>). La garantía a probar es estructural, no de timing real:
/// el reloj de pared se lee UNA sola vez (en la construcción) y nunca más — así que ningún
/// salto posterior de CLOCK_REALTIME puede afectar <see cref="RelojMonotonico.AhoraUtc"/>.
/// </summary>
public class RelojMonotonicoTests
{
    [Fact]
    public void AhoraUtc_LlamadoVariasVeces_LeeElRelojDeParedUnaSolaVez()
    {
        var lecturasDePared = 0;
        DateTime LeerPared()
        {
            lecturasDePared++;
            return new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        }

        var monotonico = 0L;
        var reloj = new RelojMonotonico(LeerPared, () => monotonico);

        reloj.AhoraUtc();
        monotonico += 1_000;
        reloj.AhoraUtc();
        monotonico += 1_000;
        reloj.AhoraUtc();

        Assert.Equal(1, lecturasDePared);
    }

    // El escenario medido en la máquina real: bajo saturación de CPU, CLOCK_REALTIME (la
    // fuente de DateTime.UtcNow) puede saltar HACIA ATRÁS ~64s. Acá se simula: el lector de
    // pared, si se volviera a invocar después del ancla, devolvería un valor MENOR (el
    // "salto"). Como AhoraUtc no vuelve a invocarlo, el salto nunca llega a filtrarse.
    [Fact]
    public void AhoraUtc_TrasUnSaltoDeRelojDeParedHaciaAtras_NuncaRetrocede()
    {
        var relojDeParedDelSistema = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        var monotonico = 0L;
        var reloj = new RelojMonotonico(() => relojDeParedDelSistema, () => monotonico);

        var ahora1 = reloj.AhoraUtc();

        // 5 segundos de tiempo MONOTÓNICO real transcurrido...
        monotonico += 5_000;
        // ...pero el reloj de pared del sistema operativo (si se leyera de nuevo) saltó
        // 64s hacia atrás -- el ajuste de NTP que se midió bajo carga.
        relojDeParedDelSistema = relojDeParedDelSistema.AddSeconds(-64);

        var ahora2 = reloj.AhoraUtc();

        Assert.True(ahora2 >= ahora1,
            $"AhoraUtc no debe retroceder pese al salto simulado: ahora1={ahora1:O}, ahora2={ahora2:O}");
        Assert.Equal(TimeSpan.FromSeconds(5), ahora2 - ahora1);
    }

    [Fact]
    public void AhoraUtc_ElAnclaEsElValorInicialDeParedMasElTranscursoMonotonico()
    {
        var ancla = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        var monotonico = 500L;
        var reloj = new RelojMonotonico(() => ancla, () => monotonico);

        monotonico += 250;

        Assert.Equal(ancla.AddMilliseconds(250), reloj.AhoraUtc());
    }

    // GUARDIÁN de la resolución del reloj de PRODUCCIÓN (ctor sin parámetros). No alcanza
    // con que la fuente monótona no retroceda: tiene que avanzar FINO. La comparación de
    // revocación es "emitidoEn >= minimo", así que dos eventos que leen el mismo valor de
    // reloj comparan como "no revocado" -- un token sobrevive a su propia revocación.
    //
    // Con Environment.TickCount64 (CLOCK_MONOTONIC_COARSE) este reloj avanzaba a saltos de
    // 3-4 ms MEDIDOS en esta máquina, y ese agujero hacía fallar de forma intermitente a
    // DeleteUsuario_ConTokenAdmin_RevocaElTokenViejoDelUsuarioDeshabilitado con 403 (token
    // aceptado) en vez de 401 (token revocado). Con Stopwatch.GetTimestamp()
    // (CLOCK_MONOTONIC, sin COARSE) el paso es de microsegundos.
    //
    // El umbral es 1 ms porque es el límite útil: por debajo de esa marca el truncado a
    // milisegundos del claim "iat" pasa a ser el factor que manda, no el reloj.
    //
    // bugfix/backups-endpoint-tests-flaky: acá el reloj de pared NO es incidental, es el SUJETO
    // bajo prueba (RelojMonotonico lo usa como ancla) -- lo que hay que sacar es sólo el reloj de
    // pared que acota la VENTANA de recolección del test, no lo que el test muestrea. La ventana
    // de 2s se mide con Stopwatch (monotónico); lo muestreado sigue siendo reloj.AhoraUtc(), el
    // reloj de producción bajo prueba, sin tocar. Deliberadamente NO se delega en
    // EsperaMonotonica.HastaAsync: ese helper duerme (Task.Delay) entre sondeos, y acá hace falta
    // lo opuesto -- un loop AJUSTADO, sin dormir, para poder detectar pasos por debajo del
    // milisegundo. Un Task.Delay(20) entre muestras haría que CUALQUIER reloj pareciera avanzar
    // "fino" y el test dejaría de medir lo que dice medir.
    [Fact]
    public void AhoraUtc_DeProduccion_AvanzaConResolucionMejorQueUnMilisegundo()
    {
        var reloj = new RelojMonotonico();

        var pasoMinimo = TimeSpan.MaxValue;
        var anterior = reloj.AhoraUtc();
        var cronometro = Stopwatch.StartNew();
        var cambios = 0;

        while (cambios < 50 && cronometro.Elapsed < TimeSpan.FromSeconds(2))
        {
            var actual = reloj.AhoraUtc();
            if (actual == anterior)
                continue;

            var paso = actual - anterior;
            if (paso < pasoMinimo)
                pasoMinimo = paso;

            anterior = actual;
            cambios++;
        }

        Assert.True(cambios > 0, "El reloj de producción nunca avanzó en 2 segundos.");
        Assert.True(pasoMinimo < TimeSpan.FromMilliseconds(1),
            $"El reloj de producción avanza a saltos de {pasoMinimo.TotalMilliseconds} ms. " +
            "Con una granularidad de 1 ms o peor, una revocación y una emisión dentro del " +
            "mismo salto son indistinguibles y el token sobrevive a su revocación.");
    }
}
