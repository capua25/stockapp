using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Lo que bindea la vista genérica de un catálogo: filas uniformes, búsqueda en el cliente, selección y
/// acciones. Cada catálogo concreto hereda de <see cref="CatalogoListadoViewModel{T}"/> y declara sus textos y columnas.
/// </summary>
public abstract partial class CatalogoListadoViewModel : ViewModelBase
{
    private int _cantidadVisible;

    protected CatalogoListadoViewModel()
    {
        FilasView = new DataGridCollectionView(Filas) { Filter = EsVisible };
        Filas.CollectionChanged += (_, _) => RecalcularVisibles();
    }

    public abstract ClaveCatalogo Clave { get; }

    /// <summary>Título del catálogo (p. ej. "Unidades de medida").</summary>
    public abstract string Titulo { get; }

    /// <summary>Texto del botón de alta (p. ej. "Nueva unidad").</summary>
    public abstract string TextoNuevo { get; }

    /// <summary>"Inactivo" o "Inactiva" según el género del catálogo.</summary>
    public abstract string TextoInactivo { get; }

    /// <summary>Texto que se muestra cuando el servidor rechaza la carga (403). Queda en <see cref="ViewModelBase.MensajeSinPermiso"/>.</summary>
    public abstract string TextoSinPermiso { get; }

    /// <summary>Cómo se llama un elemento del catálogo en el pie de conteo ("categoría"); el plural va en <see cref="PiePlural"/>.</summary>
    public abstract string PieSingular { get; }

    public abstract string PiePlural { get; }

    /// <summary>Lugar de NOMBRE entre las columnas de la grilla (0 = primera). Rubros de gasto muestra CÓDIGO antes.</summary>
    public virtual int PosicionNombre => 0;

    /// <summary>Título en minúscula para las frases de los estados ("unidades de medida", "líneas POA").</summary>
    public string TituloMinuscula => string.IsNullOrEmpty(Titulo) ? Titulo : char.ToLowerInvariant(Titulo[0]) + Titulo[1..];

    public string TextoCargando => $"Cargando {TituloMinuscula}…";

    public string TituloSinDatos => $"Todavía no hay {TituloMinuscula}";

    public string MensajeSinDatos => $"Tocá {TextoNuevo} para cargar el primero.";

    /// <summary>"6 categorías": lo visible, con singular y plural propios del catálogo.</summary>
    public string TextoPie => $"{CantidadVisible} {(CantidadVisible == 1 ? PieSingular : PiePlural)}";

    /// <summary>Columnas además de NOMBRE (0 a 4), en el orden en que se muestran.</summary>
    public abstract IReadOnlyList<ColumnaCatalogo> Columnas { get; }

    public ObservableCollection<FilaCatalogo> Filas { get; } = new();

    public DataGridCollectionView FilasView { get; }

    [ObservableProperty]
    private string _busqueda = "";

    [ObservableProperty]
    private FilaCatalogo? _filaSeleccionada;

    /// <summary>Filas que pasan el filtro de la búsqueda: es lo que cuenta el pie.</summary>
    public int CantidadVisible
    {
        get => _cantidadVisible;
        private set
        {
            if (SetProperty(ref _cantidadVisible, value))
                OnPropertyChanged(nameof(TextoPie));
        }
    }

    /// <summary>Hay filas pero la búsqueda las oculta todas (distinto de "todavía no hay nada").</summary>
    public bool SinCoincidencias => Filas.Count > 0 && CantidadVisible == 0;

    public abstract IAsyncRelayCommand NuevoCommand { get; }
    public abstract IAsyncRelayCommand EditarCommand { get; }
    public abstract IAsyncRelayCommand BajaCommand { get; }

    public abstract Task CargarAsync();

    /// <summary>La vista muestra "No se pudo conectar con el servidor" con Reintentar.</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Se llama cuando cambia la fila seleccionada, para que el VM tipado sincronice su entidad.</summary>
    protected abstract void AlCambiarFilaSeleccionada(FilaCatalogo? fila);

    partial void OnFilaSeleccionadaChanged(FilaCatalogo? value) => AlCambiarFilaSeleccionada(value);

    partial void OnBusquedaChanged(string value)
    {
        FilasView.Refresh();
        RecalcularVisibles();
        // Si la búsqueda esconde la fila seleccionada, la selección se limpia: Editar y Dar de baja no pueden
        // actuar sobre una fila que no se ve.
        if (FilaSeleccionada is { } seleccionada && !EsVisible(seleccionada))
            FilaSeleccionada = null;
    }

    /// <summary>Predicado del filtro: la grilla y el pie usan el mismo, así lo que se cuenta es lo que se ve.</summary>
    internal bool EsVisible(object item)
    {
        if (item is not FilaCatalogo fila)
            return false;
        var texto = Busqueda?.Trim();
        if (string.IsNullOrEmpty(texto))
            return true;
        return Coincide(fila.Valor0, texto) || Coincide(fila.Valor1, texto) || Coincide(fila.Valor2, texto)
            || Coincide(fila.Valor3, texto) || Coincide(fila.Valor4, texto);
    }

    private static bool Coincide(string? valor, string texto)
        => !string.IsNullOrEmpty(valor)
           && CultureInfo.InvariantCulture.CompareInfo.IndexOf(valor, texto,
                  CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    protected void RecalcularVisibles()
    {
        CantidadVisible = Filas.Count(f => EsVisible(f));
        OnPropertyChanged(nameof(SinCoincidencias));
    }
}

/// <summary>
/// Base de los 10 listados de catálogo: carga protegida (403 y servidor caído sin modal), consulta vigente,
/// búsqueda, selección y Nuevo / Editar / Dar de baja. Cada catálogo aporta el servicio, los textos, los valores
/// de las columnas y a dónde navegan Nuevo y Editar.
/// </summary>
public abstract partial class CatalogoListadoViewModel<T> : CatalogoListadoViewModel where T : class
{
    private readonly IConfirmacionService _confirmacion;
    private readonly ConsultaVigente _consultas = new();
    private T? _itemSeleccionado;
    private bool _sincronizandoSeleccion;

    protected CatalogoListadoViewModel(INavigationService navigation, IConfirmacionService confirmacion)
    {
        Navigation    = navigation;
        _confirmacion = confirmacion;
    }

    protected INavigationService Navigation { get; }

    public ObservableCollection<T> Items { get; } = new();

    public T? ItemSeleccionado
    {
        get => _itemSeleccionado;
        set
        {
            if (!SetProperty(ref _itemSeleccionado, value))
                return;
            if (!_sincronizandoSeleccion)
            {
                _sincronizandoSeleccion = true;
                try { FilaSeleccionada = value is null ? null : Filas.FirstOrDefault(f => ReferenceEquals(f.Entidad, value)); }
                finally { _sincronizandoSeleccion = false; }
            }
            EditarCommand.NotifyCanExecuteChanged();
            BajaCommand.NotifyCanExecuteChanged();
        }
    }

    protected abstract Task<IReadOnlyList<T>> ListarAsync();
    protected abstract Task DarDeBajaAsync(T item);
    protected abstract string MensajeConfirmarBaja(T item);
    protected abstract bool EsActivo(T item);

    /// <summary>Nombre primero, después un valor por cada columna declarada.</summary>
    protected abstract IReadOnlyList<string?> Valores(T item);

    protected abstract void NavegarANuevo();
    protected abstract void NavegarAEditar(T item);

    public override IAsyncRelayCommand NuevoCommand => field ??= new AsyncRelayCommand(NuevoAsync);

    public override IAsyncRelayCommand EditarCommand => field ??= new AsyncRelayCommand(EditarAsync, PuedeActuar);

    public override IAsyncRelayCommand BajaCommand => field ??= new AsyncRelayCommand(BajaAsync, PuedeActuar);

    private bool PuedeActuar() => ItemSeleccionado is { } item && EsActivo(item);

    protected override void AlCambiarFilaSeleccionada(FilaCatalogo? fila)
    {
        if (_sincronizandoSeleccion)
            return;
        _sincronizandoSeleccion = true;
        try { ItemSeleccionado = fila?.Entidad as T; }
        finally { _sincronizandoSeleccion = false; }
    }

    /// <summary>Sin servidor el listado queda vacío: el reintento no puede mostrar las filas de la consulta anterior.</summary>
    protected override void AlPerderConexion() => Vaciar();

    private void Vaciar()
    {
        ItemSeleccionado = null;
        Items.Clear();
        Filas.Clear();
    }

    /// <summary>
    /// Pide turno: si mientras espera empezó otra carga, su resultado se descarta (ConsultaVigente). Un 403 deja
    /// <see cref="ViewModelBase.SinPermiso"/> con <see cref="CatalogoListadoViewModel.TextoSinPermiso"/>; un servidor caído,
    /// <see cref="ViewModelBase.SinConexion"/>; ninguno abre un modal.
    /// </summary>
    public override async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            IReadOnlyList<T> resultados;
            try
            {
                resultados = await ListarAsync();
            }
            catch (UnauthorizedAccessException)
            {
                if (turno.EsLaUltima)
                    Vaciar();
                throw;
            }
            if (!turno.EsLaUltima)
                return;

            ItemSeleccionado = null;
            Items.Clear();
            Filas.Clear();
            foreach (var item in resultados)
            {
                Items.Add(item);
                Filas.Add(new FilaCatalogo(item, EsActivo(item), Valores(item)));
            }
        }, TextoSinPermiso, turno);
    }

    private async Task NuevoAsync() => await Task.Run(NavegarANuevo);

    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionado = ItemSeleccionado;
        await Task.Run(() => NavegarAEditar(seleccionado));
    }

    /// <summary>
    /// Da de baja el seleccionado, previa confirmación. Las excepciones de dominio esperables (ya está inactivo, o
    /// fue borrado por otra sesión) NO deben propagar: llegaban sin manejar al dispatcher de Avalonia y
    /// crasheaban la app. Después recarga y la búsqueda escrita sigue aplicada.
    /// </summary>
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var seleccionado = ItemSeleccionado;
        var confirmar = await _confirmacion.PreguntarAsync(MensajeConfirmarBaja(seleccionado));
        if (!confirmar) return;

        try
        {
            await DarDeBajaAsync(seleccionado);
            await CargarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
}
