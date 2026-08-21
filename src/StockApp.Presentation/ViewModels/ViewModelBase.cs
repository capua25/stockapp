using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StockApp.Presentation.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    private bool _sinPermiso;
    private string? _mensajeSinPermiso;

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
