namespace StockApp.Infrastructure.Tests.Fixtures;

/// <summary>
/// Lock entre PROCESOS (no solo entre threads) para serializar POR COMPLETO la vida de
/// los contenedores Testcontainers de StockApp.Api.Tests y StockApp.Infrastructure.Tests.
/// Causa raíz documentada en .superpowers/sdd/flakiness-investigacion.md (Problema B):
/// `dotnet test StockApp.sln` levanta ambos proyectos como procesos `dotnet test`
/// separados, cada uno con su propio Resource Reaper (Ryuk) -- singleton por proceso,
/// no coordinado entre procesos. Ryuk tiene un timeout FIJO de 60s (no configurable
/// desde este repo, confirmado decompilando Testcontainers.dll) para completar el
/// handshake TCP de arranque. Bajo contención de CPU/Docker (dos procesos arrancando
/// SUS PROPIOS contenedores/Ryuk AL MISMO TIEMPO), ese handshake puede no completar a
/// tiempo en alguno de los dos -> ResourceReaperException("Initialization has been
/// cancelled."), que tira abajo toda la collection que dependía de ese fixture.
///
/// QUÉ GARANTIZA ESTE LOCK (y qué NO -- ver .superpowers/sdd/flakiness-fix.md para la
/// evidencia real detrás de cada punto):
/// 1) Un lock que solo envuelve la llamada a `_container.StartAsync()` NO alcanza:
///    serializa el instante del handshake, pero el contenedor Ryuk de cada proceso
///    sigue vivo mientras ese proceso corre sus tests, así que igual terminaban
///    coexistiendo. Por eso este lock se retiene durante la vida COMPLETA del fixture
///    (desde que arranca el contenedor hasta que lo dispone), no solo el arranque.
/// 2) Con eso, lo que el lock SÍ garantiza -- y es lo que importa para la causa raíz --
///    es que nunca hay DOS HANDSHAKES ACTIVOS compitiendo por CPU/Docker al mismo
///    tiempo (que es el mecanismo real de la excepción): el segundo proceso no arranca
///    su contenedor hasta que el primero ya terminó de disponer el suyo.
/// 3) Lo que NO garantiza es que el contenedor Ryuk del primer proceso ya haya
///    desaparecido de `docker ps` en el instante en que el segundo arranca el suyo --
///    Ryuk no muere en el instante en que se dispone el último contenedor que lo usaba,
///    sigue vivo bajando solo, y ese tiempo resultó MUY variable en este entorno (se
///    midió desde ~10s hasta más de 4 minutos en corridas distintas). Se intentó primero
///    un `Task.Delay` fijo antes de soltar el lock (no confiable: 20s alcanzaba a veces,
///    no alcanzaba otras) y después un polling activo a `docker ps` esperando a que
///    Ryuk desapareciera (tampoco sirvió: el techo de espera se agotaba seguido, y
///    encima podía agregar minutos a la suite sin necessariamente resolver nada). Ambos
///    enfoques se descartaron a propósito -- ver esa evidencia en el reporte. Un Ryuk
///    "viejo" que ya terminó su propio handshake y está bajando solo NO compite por
///    CPU/Docker de la misma forma que uno recién arrancado, así que ese solapamiento
///    residual (Ryuk viejo inactivo + Ryuk nuevo arrancando) no reproduce el mecanismo
///    real de la falla -- a diferencia del estado sin este lock, donde SÍ podían
///    coexistir DOS handshakes activos.
///
/// Se eligió un lock de archivo (en vez de, por ejemplo, limitar la concurrencia de
/// MSBuild al invocar `dotnet test StockApp.sln`) porque no depende de CÓMO se invoca
/// la suite: funciona igual si se corre el .sln completo, cada proyecto por separado
/// en paralelo, desde el IDE, o desde un pipeline de CI -- la garantía vive en el
/// código del fixture, no en un flag que alguien puede omitir. Se probó también forzar
/// esto vía un `MSBuild.rsp` con `-maxcpucount:1` en la raíz del repo (que MSBuild lee
/// automáticamente sin que nadie tenga que pasar el flag) -- se descartó porque `dotnet
/// test` agrega su propio `-maxcpucount` (sin valor, "todos los cores") que pisaba el
/// nuestro, y en la práctica NO serializaba nada (confirmado con `docker ps`).
/// </summary>
internal static class LockInicializacionContenedores
{
    // Mismo archivo para los dos proyectos de test (mismo nombre fijo bajo el temp del
    // SO) -- el lock tiene que ser visto por AMBOS procesos (Api.Tests e
    // Infrastructure.Tests) para serializarlos entre sí, no alcanza con que cada
    // proyecto tenga su propio archivo.
    private static readonly string RutaArchivoLock =
        Path.Combine(Path.GetTempPath(), "stockapp-testcontainers-init.lock");

    // Generoso a propósito: con el lock cubriendo la vida completa del fixture, el
    // proceso que espera puede tener que aguantar la corrida ENTERA del otro proyecto
    // (hoy, ~1-2 minutos cada uno).
    private static readonly TimeSpan TimeoutEspera = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IntervaloReintento = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Adquiere el lock exclusivo entre procesos, por polling (abrir el archivo con
    /// FileShare.None falla con IOException mientras otro proceso lo tiene abierto).
    /// Devuelve un <see cref="IDisposable"/> que hay que retener y disponer recién
    /// cuando el contenedor de ESTE fixture ya fue dispuesto (no antes) -- normalmente
    /// desde IAsyncLifetime.DisposeAsync.
    /// </summary>
    public static async Task<IDisposable> AdquirirAsync()
    {
        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(
                    RutaArchivoLock,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException)
            {
                if (cronometro.Elapsed > TimeoutEspera)
                {
                    throw new TimeoutException(
                        $"No se pudo obtener el lock de inicialización de contenedores " +
                        $"('{RutaArchivoLock}') después de {TimeoutEspera}. Otro proceso de " +
                        "test (Api.Tests / Infrastructure.Tests) lo tiene tomado y no lo " +
                        "liberó a tiempo -- puede estar colgado.");
                }

                await Task.Delay(IntervaloReintento);
            }
        }
    }
}
