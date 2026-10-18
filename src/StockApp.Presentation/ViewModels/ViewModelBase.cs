using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.ApiClient;

namespace StockApp.Presentation.ViewModels;

public abstract class ViewModelBase : ObservableObject, IConErroresDeEntrada
{
    /// <summary>Explicación del botón de guardar deshabilitado por <see cref="HayErroresDeEntrada"/>.</summary>
    public const string MensajeErroresDeEntrada = "Corregí los campos marcados en rojo para continuar.";

    private bool _hayErroresDeEntrada;
    private bool _sinPermiso;
    private string? _mensajeSinPermiso;
    private bool _sinConexion;
    private Func<Task>? _cargaParaReintentar;
    private int _cargasEnCurso;
    private readonly List<ConsultaVigente.Turno> _cargasConTurnoEnCurso = new();
    // La lista la tocan, en la práctica, dos hilos: el que dispara la carga y el continuation del debounce
    // (Task.Delay vuelve en el pool cuando no hay SynchronizationContext). List<T> no es thread-safe.
    private readonly object _cargasConTurnoLock = new();

    /// <summary>
    /// True mientras algún campo de la vista tiene un error de entrada que vive SOLO en la View
    /// (texto que el converter rechaza, ej. "5.4", o un NumericUpDown con "20a5"): ese texto
    /// nunca llegó a la propiedad del ViewModel, que conserva el valor ANTERIOR. Lo escribe
    /// <see cref="StockApp.Presentation.Behaviors.ErroresDeEntradaBehavior"/> (global en el tema).
    /// REGLA: todo comando de guardar/confirmar (o de filtrar con lo tipeado) se crea con
    /// <see cref="ComandoDeGuardado(Func{Task}, Func{bool}?)"/>, que compone <c>!HayErroresDeEntrada</c>
    /// en su CanExecute y lo re-notifica solo; si no, guarda en silencio el valor viejo (bug de
    /// integridad 2026-10-01). Lo vigila <c>ComandosDeGuardadoGuardianTests</c> (Presentation.Tests).
    /// </summary>
    public bool HayErroresDeEntrada
    {
        get => _hayErroresDeEntrada;
        set
        {
            if (!SetProperty(ref _hayErroresDeEntrada, value))
                return;

            OnPropertyChanged(nameof(MotivoBloqueoPorErrores));
            foreach (var comando in _comandosDeGuardado)
                comando.NotifyCanExecuteChanged();
            AlCambiarErroresDeEntrada();
        }
    }

    private readonly List<IRelayCommand> _comandosDeGuardado = new();

    /// <summary>Comandos creados con <see cref="ComandoDeGuardado(Func{Task}, Func{bool}?)"/>: los
    /// que se bloquean con <see cref="HayErroresDeEntrada"/>. El tema los usa para ponerle al botón
    /// el tooltip <see cref="MotivoBloqueoPorErrores"/>.</summary>
    public bool EsComandoDeGuardado(ICommand? comando)
        => comando is not null && _comandosDeGuardado.Any(c => ReferenceEquals(c, comando));

    /// <summary>
    /// Único camino para crear un comando de guardar/confirmar (o de filtrar con lo tipeado): su
    /// CanExecute es <c>!HayErroresDeEntrada &amp;&amp; puede()</c> y se re-notifica cada vez que
    /// cambia <see cref="HayErroresDeEntrada"/>, sin cableado en el ViewModel concreto. Mismo
    /// comportamiento que el <c>[RelayCommand(CanExecute = ...)]</c> que reemplaza (sin ejecuciones
    /// concurrentes). Uso: <c>public IAsyncRelayCommand GuardarCommand =&gt; field ??= ComandoDeGuardado(GuardarAsync, PuedeGuardar);</c>
    /// </summary>
    protected IAsyncRelayCommand ComandoDeGuardado(Func<Task> ejecutar, Func<bool>? puede = null)
        => Registrar(new AsyncRelayCommand(ejecutar, () => !HayErroresDeEntrada && (puede?.Invoke() ?? true)));

    /// <inheritdoc cref="ComandoDeGuardado(Func{Task}, Func{bool}?)"/>
    protected IRelayCommand ComandoDeGuardado(Action ejecutar, Func<bool>? puede = null)
        => Registrar(new RelayCommand(ejecutar, () => !HayErroresDeEntrada && (puede?.Invoke() ?? true)));

    private T Registrar<T>(T comando) where T : IRelayCommand
    {
        _comandosDeGuardado.Add(comando);
        return comando;
    }

    /// <summary>Tooltip del botón de guardar: <see cref="MensajeErroresDeEntrada"/> mientras
    /// <see cref="HayErroresDeEntrada"/>; null (sin tooltip) si no.</summary>
    public string? MotivoBloqueoPorErrores => HayErroresDeEntrada ? MensajeErroresDeEntrada : null;

    /// <summary>Hook para lo que NO es un comando (los de <see cref="ComandoDeGuardado(Func{Task}, Func{bool}?)"/>
    /// ya se re-notifican solos): ej. un mensaje que explica el bloqueo.</summary>
    protected virtual void AlCambiarErroresDeEntrada()
    {
    }

    /// <summary>
    /// True cuando la última carga protegida por <see cref="EjecutarCargaProtegidaAsync(Func{Task}, string)"/> fue
    /// rechazada por falta de permiso (403/401 del servidor, ver
    /// StockApp.ApiClient.ApiErrores.AsegurarExitoAsync — nacimiento único de
    /// UnauthorizedAccessException). La vista bindea esto (junto con
    /// <see cref="MensajeSinPermiso"/>) al control EstadoVacio para que la pantalla deje de
    /// quedar muda en vez de mostrar un modal: el aviso global ya existe
    /// (AuthTokenHandler.SendAsync -&gt; ApiSession.AccesoRevocado, dispara ANTES de que esta
    /// excepción del lado cliente exista) y agregar un segundo aviso acá duplicaría el modal
    /// (bug ya arreglado una vez en este proyecto).
    /// </summary>
    public bool SinPermiso
    {
        get => _sinPermiso;
        private set => SetProperty(ref _sinPermiso, value);
    }

    /// <summary>
    /// True cuando la última carga protegida por <see cref="EjecutarCargaProtegidaAsync(Func{Task}, string)"/> no
    /// llegó al servidor (<see cref="ServidorNoDisponibleException"/>: conexión rechazada, host inalcanzable o
    /// timeout). Sin esto, el listado quedaba vacío y la vista afirmaba "no hay datos" cuando en realidad no se
    /// pudo consultar. La vista muestra en la card un EstadoVacio de error con un botón
    /// <see cref="ReintentarCargaCommand"/>, que reemplaza al modal global (ese caso ya no escapa). Solo lo marcan
    /// los ViewModels cuya vista lo muestra (<see cref="AvisaSinConexionEnLinea"/>).
    /// </summary>
    public bool SinConexion
    {
        get => _sinConexion;
        private set => SetProperty(ref _sinConexion, value);
    }

    /// <summary>
    /// True si la vista muestra <see cref="SinConexion"/> en línea con "Reintentar". En false (por defecto) un
    /// servidor caído en la carga protegida se repropaga como siempre y lo informa el modal global: una vista que no
    /// muestra el estado quedaría muda con la excepción tragada.
    /// </summary>
    protected virtual bool AvisaSinConexionEnLinea => false;

    /// <summary>
    /// Se llama cuando la consulta vigente queda en <see cref="SinConexion"/> (una consulta vieja que falla no llega
    /// acá: convención 23). El VM dueño de un listado lo sobrescribe para vaciarlo: si no, al reintentar
    /// (<see cref="SinConexion"/> vuelve a false hasta ~15 s de timeout) la grilla mostraría las filas de la consulta anterior.
    /// La base no conoce el listado, por eso es un gancho y no el vaciado en sí.
    /// </summary>
    protected virtual void AlPerderConexion()
    {
    }

    /// <summary>Vuelve a correr la carga protegida que falló por servidor caído (botón "Reintentar" del estado
    /// <see cref="SinConexion"/>). Sin una carga fallida no hace nada.</summary>
    public IAsyncRelayCommand ReintentarCargaCommand => field ??= new AsyncRelayCommand(ReintentarCargaAsync);

    private Task ReintentarCargaAsync() => _cargaParaReintentar is { } cargaFallida ? ReintentarAsync(cargaFallida) : Task.CompletedTask;

    /// <summary>
    /// Qué hace "Reintentar" cuando hay una carga fallida pendiente. Por defecto repite esa carga tal cual. Un listado con
    /// filtros lo sobrescribe para buscar con el filtro que está en pantalla en ese momento (convención 18), no con el
    /// que tenía la consulta que falló.
    /// </summary>
    protected virtual Task ReintentarAsync(Func<Task> cargaFallida) => cargaFallida();

    /// <summary>Mensaje para <c>EstadoVacio.Mensaje</c> cuando <see cref="SinPermiso"/> es true.</summary>
    public string? MensajeSinPermiso
    {
        get => _mensajeSinPermiso;
        private set => SetProperty(ref _mensajeSinPermiso, value);
    }

    /// <summary>
    /// True mientras corre al menos una carga (las de <see cref="EjecutarCargaProtegidaAsync(Func{Task}, string)"/> y las que un
    /// ViewModel marque con <see cref="EmpezarCarga"/>/<see cref="TerminarCarga"/>). Es un contador y no un
    /// bool: una búsqueda con debounce y una recarga pueden superponerse, y la primera en terminar no puede
    /// apagar el estado de la otra. La vista lo usa para decir "Cargando…" en vez de afirmar "no hay datos"
    /// antes de tenerlos. Notifica solo cuando cambia de valor.
    /// Las consultas con turno (<see cref="ConsultaVigente"/>) no suman al contador: "solo la última consulta
    /// decide", así que una vieja colgada no mantiene prendido el "Cargando…" cuando la vigente ya terminó
    /// (ver <see cref="EmpezarCargaConTurno"/>).
    /// </summary>
    public bool Cargando => _cargasEnCurso > 0 || HayCargaConTurnoVigente();

    private bool HayCargaConTurnoVigente()
    {
        lock (_cargasConTurnoLock)
            return _cargasConTurnoEnCurso.Exists(t => t.EsLaUltima);
    }

    /// <summary>Marca el inicio de una carga (ver <see cref="Cargando"/>). Cada llamada pide su <see cref="TerminarCarga"/>.</summary>
    protected void EmpezarCarga()
    {
        if (++_cargasEnCurso == 1)
            OnPropertyChanged(nameof(Cargando));
    }

    /// <summary>
    /// Inicio de una consulta con turno: <see cref="Cargando"/> la cuenta solo mientras sea la VIGENTE. Cada consulta
    /// nueva reemplaza a las anteriores: las viejas, aunque sigan colgadas, ya no importan (su resultado se descarta).
    /// </summary>
    private void EmpezarCargaConTurno(ConsultaVigente.Turno turno)
    {
        var antes = Cargando;
        lock (_cargasConTurnoLock)
            _cargasConTurnoEnCurso.Add(turno);
        if (antes != Cargando) OnPropertyChanged(nameof(Cargando));
    }

    /// <summary>Fin de una consulta con turno: una vieja que termina no toca el estado de la que sigue en curso.</summary>
    private void TerminarCargaConTurno(ConsultaVigente.Turno turno)
    {
        var antes = Cargando;
        lock (_cargasConTurnoLock)
            _cargasConTurnoEnCurso.Remove(turno);
        if (antes != Cargando) OnPropertyChanged(nameof(Cargando));
    }

    /// <summary>Marca el fin de una carga empezada con <see cref="EmpezarCarga"/>; va en un finally.</summary>
    protected void TerminarCarga()
    {
        if (_cargasEnCurso > 0 && --_cargasEnCurso == 0)
            OnPropertyChanged(nameof(Cargando));
    }

    /// <summary>
    /// Los permisos del usuario cambiaron en caliente (un 403 refrescó el cache). Lo invoca
    /// <see cref="ShellMainViewModel.NotificarPermisosActualizados"/> sobre la pantalla abierta. Las
    /// <c>Puede*</c> calculadas sobre ICurrentSession (que no notifica) no tienen forma de enterarse
    /// solas, así que la base notifica TODAS las propiedades (<c>string.Empty</c> = "cambió todo", lo
    /// que hace que los bindings re-lean el VM): ninguna pantalla necesita un override propio.
    /// </summary>
    public virtual void NotificarPermisosActualizados() => OnPropertyChanged(string.Empty);

    /// <summary>Marca el estado "sin permiso" (ver <see cref="SinPermiso"/>) con el mensaje que la
    /// vista va a mostrar en EstadoVacio. Expuesto protected para el caso puntual de
    /// HistorialImportacionesViewModel.CargarAsync, que ya tenía su propio catch con filtro
    /// <c>when</c> antes de este fix y solo necesitaba ampliarlo (no envolver todo el método con
    /// <see cref="EjecutarCargaProtegidaAsync(Func{Task}, string)"/>, que hubiera dejado dos mecanismos de protección
    /// compitiendo en el mismo método).</summary>
    protected void MarcarSinPermiso(string mensaje)
    {
        SinPermiso = true;
        MensajeSinPermiso = mensaje;
    }

    /// <summary>Limpia el estado "sin permiso" (escenario de reintento: la vista puede volver a
    /// disparar la carga tras reactivar el permiso).</summary>
    protected void LimpiarSinPermiso()
    {
        SinPermiso = false;
        MensajeSinPermiso = null;
    }

    /// <summary>
    /// Único camino correcto para proteger una carga fire-and-forget disparada desde
    /// DataContextChanged (convención del proyecto, ver Views/*.axaml.cs: el evento es void, así
    /// que el lambda que engancha es efectivamente async void y nadie observa la Task). Antes de
    /// este helper, 14 puntos de carga no atrapaban <see cref="UnauthorizedAccessException"/> y
    /// el método moría a mitad de camino dejando la pantalla vacía sin ninguna explicación (bug
    /// real, distinto de un crash: la red global de PoliticaExcepcionSilenciosa ya evita que la
    /// app se caiga, y AuthTokenHandler ya le avisa al usuario con un modal ANTES de que esta
    /// excepción del lado cliente exista — lo único que faltaba era que la pantalla no quedara
    /// muda).
    ///
    /// Atrapa SOLO UnauthorizedAccessException (mismo criterio que los ~20 ViewModels ya
    /// protegidos con <c>catch (UnauthorizedAccessException)</c> directo, ej.
    /// ProveedorListViewModel.CargarAsync) y deja al ViewModel en un estado bindeable
    /// (<see cref="SinPermiso"/>/<see cref="MensajeSinPermiso"/>) para que la vista muestre el
    /// control EstadoVacio. NO dispara ningún modal/toast/IConfirmacionService.InformarAsync — el
    /// aviso global ya existe, y agregar uno acá da DOS modales (ese bug ya se arregló una vez).
    /// Si el ViewModel opta por <see cref="AvisaSinConexionEnLinea"/>, atrapa además
    /// <see cref="ServidorNoDisponibleException"/> y deja <see cref="SinConexion"/> en true con la carga lista para
    /// <see cref="ReintentarCargaCommand"/>, también sin modal (el aviso en línea lo reemplaza). Cualquier otra
    /// excepción se repropaga tal cual: esto no es un catch-all que se trague bugs reales.
    ///
    /// También prende <see cref="Cargando"/> mientras corre (se apaga en el finally, falle o no).
    /// </summary>
    protected Task EjecutarCargaProtegidaAsync(Func<Task> cargar, string mensajeSinPermiso) =>
        EjecutarCargaCoreAsync(cargar, mensajeSinPermiso, turno: null);

    private async Task EjecutarCargaCoreAsync(Func<Task> cargar, string mensajeSinPermiso, ConsultaVigente.Turno? turno)
    {
        if (turno is { } t0) EmpezarCargaConTurno(t0); else EmpezarCarga();
        LimpiarSinPermiso();
        SinConexion = false;
        try
        {
            await cargar();
            // La carga vigente terminó bien: ya no hay nada que reintentar y el closure (con lo que captura) se suelta.
            if (turno is not { EsLaUltima: false })
                _cargaParaReintentar = null;
        }
        catch (UnauthorizedAccessException)
        {
            MarcarSinPermiso(mensajeSinPermiso);
        }
        catch (ServidorNoDisponibleException) when (AvisaSinConexionEnLinea)
        {
            _cargaParaReintentar = () => EjecutarCargaCoreAsync(cargar, mensajeSinPermiso, turno: null);
            SinConexion = true;
            AlPerderConexion();
        }
        finally
        {
            if (turno is { } t) TerminarCargaConTurno(t); else TerminarCarga();
        }
    }

    /// <summary>
    /// Igual que <see cref="EjecutarCargaProtegidaAsync(Func{Task}, string)"/> para una consulta que reemplaza el
    /// contenido de un listado (ver <see cref="ConsultaVigente"/>): un 403 o un servidor caído de una consulta que ya
    /// no es la última se descartan (si no, "Sin permiso" o "No se pudo conectar" taparían los resultados buenos de
    /// la consulta nueva; el aviso global del 403 ya salió por AuthTokenHandler). El que llama sigue siendo
    /// responsable de no aplicar un resultado viejo: después del await al servicio, <c>if (!turno.EsLaUltima) return;</c>.
    /// </summary>
    protected async Task EjecutarCargaProtegidaAsync(Func<Task> cargar, string mensajeSinPermiso, ConsultaVigente.Turno turno)
    {
        await EjecutarCargaCoreAsync(async () =>
        {
            try
            {
                await cargar();
            }
            catch (Exception ex) when (!turno.EsLaUltima
                && (ex is UnauthorizedAccessException || (ex is ServidorNoDisponibleException && AvisaSinConexionEnLinea)))
            {
            }
        }, mensajeSinPermiso, turno);
    }
}
