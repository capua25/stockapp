using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StockApp.Presentation.ViewModels;

public abstract class ViewModelBase : ObservableObject, IConErroresDeEntrada
{
    /// <summary>Explicación del botón de guardar deshabilitado por <see cref="HayErroresDeEntrada"/>.</summary>
    public const string MensajeErroresDeEntrada = "Corregí los campos marcados en rojo para continuar.";

    private bool _hayErroresDeEntrada;
    private bool _sinPermiso;
    private string? _mensajeSinPermiso;

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
    /// True cuando la última carga protegida por <see cref="EjecutarCargaProtegidaAsync"/> fue
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

    /// <summary>Mensaje para <c>EstadoVacio.Mensaje</c> cuando <see cref="SinPermiso"/> es true.</summary>
    public string? MensajeSinPermiso
    {
        get => _mensajeSinPermiso;
        private set => SetProperty(ref _mensajeSinPermiso, value);
    }

    /// <summary>Marca el estado "sin permiso" (ver <see cref="SinPermiso"/>) con el mensaje que la
    /// vista va a mostrar en EstadoVacio. Expuesto protected para el caso puntual de
    /// HistorialImportacionesViewModel.CargarAsync, que ya tenía su propio catch con filtro
    /// <c>when</c> antes de este fix y solo necesitaba ampliarlo (no envolver todo el método con
    /// <see cref="EjecutarCargaProtegidaAsync"/>, que hubiera dejado dos mecanismos de protección
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
    /// Cualquier excepción que NO sea UnauthorizedAccessException se repropaga tal cual: esto no
    /// es un catch-all que se trague bugs reales.
    /// </summary>
    protected async Task EjecutarCargaProtegidaAsync(Func<Task> cargar, string mensajeSinPermiso)
    {
        LimpiarSinPermiso();
        try
        {
            await cargar();
        }
        catch (UnauthorizedAccessException)
        {
            MarcarSinPermiso(mensajeSinPermiso);
        }
    }
}
