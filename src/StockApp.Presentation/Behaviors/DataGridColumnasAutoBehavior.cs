using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Hace que las columnas <c>Auto</c> (y las de ancho fijo) de una DataGrid con columnas
/// proporcionales (<c>*</c>) lleguen a su ancho natural cuando cambian los datos, tomando el
/// espacio de las columnas <c>*</c> sin importar en qué posición estén. Se aplica a TODAS las
/// DataGrid desde <c>Themes/DataGrid.axaml</c>, así las grillas nuevas lo heredan.
///
/// <para><b>El problema (DataGrid 12.0.1, verificado decompilando el ensamblado).</b> La grilla se
/// arma vacía y los datos llegan después. Cuando una celda Auto necesita más ancho del que la
/// columna ya determinó, <c>DataGrid.AutoSizeColumn</c> lo manda por
/// <c>DataGridColumn.Resize(userInitiated: false)</c>. Con el redimensionado habilitado
/// (<c>ActualCanUserResize</c>), ese camino solo le saca ancho a las columnas a su DERECHA
/// (<c>DecreaseColumnWidths(DisplayIndex + 1)</c>): primero a las <c>*</c> de la derecha y, si no
/// alcanza, aprieta hasta su MinWidth a las de ancho fijo de la derecha. Lo que no consigue, se
/// pierde. Encima, cuando hay columnas <c>*</c>, cada measure corre
/// <c>AdjustColumnWidths(0, CellsWidth - total)</c>, que si no entra todo aprieta a cualquier
/// columna redimensionable hasta su MinWidth: con el resize habilitado la grilla no scrollea
/// hasta que TODAS quedan en su mínimo. Por eso "Usuario" quedaba en 32px, "P. Costo" cortado
/// y la Stock de 220 en ~198.</para>
///
/// <para><b>El arreglo.</b> Después de cada pasada de layout se calcula el reparto que
/// corresponde y, si el actual se aparta, se asigna explícito:</para>
/// <list type="bullet">
/// <item>Cada columna no proporcional va a su objetivo: una Auto, a <c>Width.DesiredValue</c> (el
/// ancho natural que la propia grilla ya midió con el encabezado y las celdas realizadas); una
/// fija, a su <c>Width.Value</c>.</item>
/// <item>Si eso más el mínimo de las <c>*</c> entra en el ancho de la grilla ("modo encaja"), las
/// <c>*</c> se reparten el resto según su peso DECLARADO (el camino de Resize también les
/// deforma el peso).</item>
/// <item>Si no entra ("modo desborde"), las <c>*</c> pasan a ancho fijo en su piso. Sin columnas
/// <c>*</c>, la grilla deja de apretar y lo que no entra se ve con el scroll horizontal. Cuando
/// vuelve a entrar, recuperan su peso.</item>
/// </list>
/// <para><b>Piso de las <c>*</c>.</b> El mínimo de una columna <c>*</c> no es el
/// <c>MinColumnWidth</c> de 40 de la grilla sino <see cref="PisoProporcionalPorDefecto"/> (160):
/// en una PC de 1366x768, sin el sidebar, el Historial no entra y Producto/Comentario quedaban en
/// 40px, ilegibles. El piso efectivo es el MÁXIMO entre <see cref="PisoProporcionalProperty"/> y
/// el mínimo propio de la columna (su <c>MinWidth</c>, o el de la grilla), acotado por su
/// <c>MaxWidth</c>: un <c>MinWidth</c> mayor manda, y para bajar el piso de una columna puntual se
/// declara el attached. "Entra" = las no proporcionales en su objetivo más las <c>*</c> en su piso
/// caben en el ancho disponible; si no, las <c>*</c> quedan en el piso y la grilla scrollea, por
/// angosta que sea. El piso es solo del reparto automático: el usuario puede seguir arrastrando
/// una <c>*</c> por debajo (el redimensionado de la grilla usa su <c>MinWidth</c>).</para>
/// <para>Los anchos se asignan por el ÚNICO camino público que no pasa por <c>Resize</c>: un
/// cambio de <c>Width</c> que cambia <c>IsStar</c> se aplica tal cual
/// (<c>SetWidthInternalNoCallback</c>). Por eso cada asignación es un ida y vuelta (Star ↔ el
/// ancho real). Con el total igual al ancho de la grilla, el <c>AdjustColumnWidths</c> del
/// measure siguiente no tiene nada que mover.</para>
///
/// <para><b>Robustez.</b></para>
/// <list type="bullet">
/// <item>(a) Redimensionado manual: si el usuario arrastra un separador de esa grilla (se detecta
/// comparando los anchos al apretar y al soltar el puntero sobre un encabezado), el behavior no
/// vuelve a tocar esa grilla: los anchos pasan a ser del usuario.</item>
/// <item>(b) Sin loops: el reparto calculado es un punto fijo (aplicarlo deja la grilla en el
/// mismo estado que lo pidió) y la decisión encaja/desborde depende solo de anchos naturales,
/// mínimos y ancho de la grilla, no del reparto actual. Si algo externo igual peleara con el
/// reparto, <see cref="MaxReparacionesSeguidas"/> corta en vez de caer en el "Infinite layout
/// loop" de Avalonia.</item>
/// <item>(c) Filas que aparecen al scrollear con contenido más ancho: la grilla mide solo las
/// filas realizadas; cuando aparece una más ancha, la columna crece en ese momento, en la misma
/// pasada de render (MediaContext corre los layout pendientes antes de dibujar).</item>
/// <item>(d) Recargas, filtro y orden: <c>Width.DesiredValue</c> de una Auto nunca baja en la
/// grilla, así que las columnas no se achican de golpe ni saltan al ordenar.</item>
/// <item>(e) Las <c>*</c> conservan su peso declarado y las de ancho fijo su ancho.</item>
/// <item>(f) Columnas que miden su contenido (<see cref="ColumnaAuto"/>): al cambiar los datos se miden sobre
/// TODA la fuente y quedan en píxeles antes del reparto; respetan el arrastre del usuario igual que el resto.</item>
/// </list>
///
/// <para><b>Tope adaptable de las columnas medidas.</b> Una columna medida pide su ancho medido (con el tope de
/// <see cref="ColumnaAuto"/>), pero no empuja a las demás fuera del borde: si lo que piden todas no entra, cede
/// hasta lo que quede libre y su texto se corta con elipsis. El orden en que se cede el espacio:</para>
/// <list type="number">
/// <item>Las <c>*</c> bajan de su parte proporcional hasta su piso (el reparto de siempre).</item>
/// <item>Las columnas medidas bajan desde su ancho medido, de la más ancha a la más angosta (se nivelan hacia
/// abajo: la más ancha suele deberse a un solo valor largo, y un código o un importe cortos quedan enteros),
/// hasta el ancho de su título con la flecha de orden. Más angosta, el header se corta y la columna deja de
/// decir qué muestra.</item>
/// <item>Si ni así entra (ventana muy angosta para las fijas, los títulos y los pisos), las <c>*</c> NO bajan de
/// su piso: con una proporcional de 40 px el nombre no se lee. Queda el modo desborde de arriba, con scroll.</item>
/// </list>
/// <para>El ancho cedido depende solo de lo medido (que cambia con los datos) y del ancho de la grilla, nunca
/// del reparto actual: se recalcula al cambiar el ancho de la grilla y no se mueve al scrollear, ordenar ni
/// filtrar. Sin columnas <c>*</c> también se cede, para que una grilla así no scrollee por una columna medida.</para>
/// </summary>
public static class DataGridColumnasAutoBehavior
{
    public static readonly AttachedProperty<bool> AjustarColumnasAutoProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, DataGrid, bool>("AjustarColumnasAuto");

    /// <summary>Piso por defecto de una columna <c>*</c>: ver <see cref="PisoProporcionalProperty"/>.</summary>
    public const double PisoProporcionalPorDefecto = 160;

    /// <summary>
    /// Piso de una columna proporcional (<c>*</c>) en el reparto automático. Default
    /// <see cref="PisoProporcionalPorDefecto"/>; el efectivo es el máximo entre este valor y el
    /// mínimo propio de la columna. Declararlo en una columna puntual solo hace falta para BAJARLO.
    /// </summary>
    public static readonly AttachedProperty<double> PisoProporcionalProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, DataGridColumn, double>("PisoProporcional", PisoProporcionalPorDefecto);

    public static double GetPisoProporcional(DataGridColumn columna) => columna.GetValue(PisoProporcionalProperty);

    public static void SetPisoProporcional(DataGridColumn columna, double value) => columna.SetValue(PisoProporcionalProperty, value);

    /// <summary>Tolerancia en píxeles: diferencias menores son redondeo de layout.</summary>
    private const double Tolerancia = 0.5;

    /// <summary>
    /// Tope de reparaciones consecutivas sin una pasada "en equilibrio" en el medio. En el camino
    /// normal alcanza con una; si algo no previsto peleara con el reparto, se deja de reparar en
    /// vez de colgar la UI con un loop de layout.
    /// </summary>
    internal const int MaxReparacionesSeguidas = 8;

    private sealed class Estado
    {
        /// <summary>Peso declarado de cada columna <c>*</c>, tomado la primera vez que se la ve.</summary>
        public readonly Dictionary<DataGridColumn, double> PesosDeclarados = new();
        public bool UsuarioRedimensiono;
        public Dictionary<DataGridColumn, DataGridLength>? AnchosAlApretar;
        public int ReparacionesSeguidas;
        public bool Reparando;
        public WeakReference<DataGridRowsPresenter>? Filas;
        /// <summary>Los datos cambiaron y hay columnas que miden su contenido (ColumnaAuto) por medir.</summary>
        public bool MedicionPendiente = true;
        public INotifyCollectionChanged? Fuente;
        public NotifyCollectionChangedEventHandler? AlCambiarFuente;
        /// <summary>Último ancho medido por columna: el ancho medido solo crece mientras vive la grilla.</summary>
        public readonly Dictionary<DataGridColumn, double> AnchosMedidos = new();
        /// <summary>Hasta dónde puede ceder cada columna medida si no hay lugar (su título con la flecha de orden).</summary>
        public readonly Dictionary<DataGridColumn, double> PisosMedidos = new();
        /// <summary>Veces que se recorrió la fuente entera para medir (lo que cuesta O(filas)).</summary>
        public int RecorridosDeLaFuente;
        /// <summary>Pasadas seguidas con alguna columna marcada que no se puede medir nunca, desde el último cambio de datos.</summary>
        public int IntentosDeMedicion;
        /// <summary>Columnas ya medidas desde el último cambio de datos: un reintento no las vuelve a medir.</summary>
        public readonly HashSet<DataGridColumn> MedidasDesdeElCambio = new();
    }

    private static readonly ConditionalWeakTable<DataGrid, Estado> Estados = new();

    /// <summary>Cuántas veces el behavior recorrió la fuente de <paramref name="grid"/> para medir sus columnas.</summary>
    internal static int RecorridosDeLaFuente(DataGrid grid) => Estados.TryGetValue(grid, out var e) ? e.RecorridosDeLaFuente : 0;

    /// <summary>El ancho que midió <paramref name="columna"/> (lo que pide), aunque haya cedido por falta de lugar; null si no se midió.</summary>
    internal static double? AnchoMedido(DataGrid grid, DataGridColumn columna) =>
        Estados.TryGetValue(grid, out var e) && e.AnchosMedidos.TryGetValue(columna, out var ancho) ? ancho : null;

    /// <summary>
    /// Pasadas de layout seguidas en las que se reintenta una columna marcada que no se puede medir nunca (Header
    /// vacío o repetido, plantilla sin TextBlock) antes de dejarla hasta el próximo cambio de datos. Una que
    /// todavía no está realizada (sin header o celda) no gasta intentos: espera sin recorrer la fuente.
    /// </summary>
    internal const int MaxIntentosDeMedicion = 8;

    static DataGridColumnasAutoBehavior()
    {
        AjustarColumnasAutoProperty.Changed.AddClassHandler<DataGrid>(OnAjustarColumnasAutoChanged);
        DataGrid.ItemsSourceProperty.Changed.AddClassHandler<DataGrid>((grid, _) => ObservarFuente(grid));
    }

    public static bool GetAjustarColumnasAuto(DataGrid grid) => grid.GetValue(AjustarColumnasAutoProperty);

    public static void SetAjustarColumnasAuto(DataGrid grid, bool value) => grid.SetValue(AjustarColumnasAutoProperty, value);

    private static void OnAjustarColumnasAutoChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs e)
    {
        if (Estados.TryGetValue(grid, out var previo) && previo.Fuente is not null && previo.AlCambiarFuente is not null)
            previo.Fuente.CollectionChanged -= previo.AlCambiarFuente;
        grid.LayoutUpdated -= OnLayoutUpdated;
        grid.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        grid.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        grid.RemoveHandler(InputElement.PointerCaptureLostEvent, OnPointerCaptureLost);

        if (!e.GetNewValue<bool>())
            return;

        Estados.GetValue(grid, _ => new Estado());
        grid.LayoutUpdated += OnLayoutUpdated;
        // handledEventsToo: el encabezado marca Handled el puntero cuando arranca un arrastre.
        grid.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        grid.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        grid.AddHandler(InputElement.PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        ObservarFuente(grid);
    }

    // ── (a) Detección del redimensionado manual ───────────────────────────────────────────────

    private static bool EsEncabezado(object? source) =>
        source is Visual v && (v is DataGridColumnHeader || v.FindAncestorOfType<DataGridColumnHeader>() is not null);

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid || !Estados.TryGetValue(grid, out var estado) || !EsEncabezado(e.Source))
            return;
        estado.AnchosAlApretar = grid.Columns.ToDictionary(c => c, c => c.Width);
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e) => TerminarGesto(sender);

    private static void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => TerminarGesto(sender);

    private static void TerminarGesto(object? sender)
    {
        if (sender is not DataGrid grid || !Estados.TryGetValue(grid, out var estado) || estado.AnchosAlApretar is null)
            return;

        // Un click para ordenar no cambia ningún ancho de manera sincrónica (el reordenamiento se
        // mide en la pasada de layout siguiente); un arrastre del separador, sí (Resize con
        // userInitiated: true dentro del PointerMoved).
        var anchos = estado.AnchosAlApretar;
        estado.AnchosAlApretar = null;
        if (grid.Columns.Any(c => !anchos.TryGetValue(c, out var antes) || antes != c.Width))
            estado.UsuarioRedimensiono = true;
    }

    // ── Columnas que miden su contenido (ColumnaAuto) ─────────────────────────────────────────

    /// <summary>
    /// Escucha la colección que se mide (<see cref="ColumnaAuto.Fuente"/>): de un DataGridCollectionView, su
    /// SourceCollection y no la vista, que re-emite como Reset cada orden y cada filtro aunque las filas medidas
    /// sean las mismas. Una carga, un Clear o un Add dejan la medición pendiente; los Clear y Add de una carga se
    /// coalescen solos: se mide una vez, en la pasada de layout siguiente.
    /// </summary>
    private static void ObservarFuente(DataGrid grid)
    {
        if (!Estados.TryGetValue(grid, out var estado))
            return;
        if (estado.Fuente is not null && estado.AlCambiarFuente is not null)
            estado.Fuente.CollectionChanged -= estado.AlCambiarFuente;
        estado.AlCambiarFuente ??= (_, _) => PedirMedicion(grid, estado);
        estado.Fuente = ColumnaAuto.Fuente(grid.ItemsSource) as INotifyCollectionChanged;
        if (estado.Fuente is not null)
            estado.Fuente.CollectionChanged += estado.AlCambiarFuente;
        PedirMedicion(grid, estado);
    }

    private static void PedirMedicion(DataGrid grid, Estado estado)
    {
        estado.MedicionPendiente = true;
        estado.IntentosDeMedicion = 0;
        estado.MedidasDesdeElCambio.Clear();
        grid.InvalidateMeasure();
    }

    /// <summary>
    /// Fija en píxeles el ancho de cada columna Auto marcada con ColumnaAuto.Medir (una fija o * marcada conserva
    /// su ancho; las ya medidas quedaron en píxeles). Grilla vacía: no se toca nada. Una columna que no se puede
    /// medir no frena a las demás y se reintenta en las pasadas siguientes, sin volver a medir las que ya se
    /// midieron: si todavía no está realizada, hasta que lo esté; si no se puede medir nunca (Header vacío o
    /// repetido, plantilla sin TextBlock), hasta <see cref="MaxIntentosDeMedicion"/>. Con ancho en píxeles, el
    /// reparto de abajo la trata como una columna fija.
    /// </summary>
    private static void MedirColumnas(DataGrid grid, Estado estado)
    {
        var marcadas = grid.Columns
            .Where(c => c.IsVisible && ColumnaAuto.GetMedir(c) && (c.Width.IsAuto || estado.AnchosMedidos.ContainsKey(c)))
            .Where(c => !estado.MedidasDesdeElCambio.Contains(c))
            .ToList();
        if (marcadas.Count == 0 || !ColumnaAuto.HayFilas(grid.ItemsSource))
        {
            estado.MedicionPendiente = false;
            return;
        }

        IReadOnlyList<object>? items = null;
        IReadOnlyList<object> Filas()
        {
            if (items is null)
            {
                estado.RecorridosDeLaFuente++;
                items = ColumnaAuto.Items(grid.ItemsSource);
            }
            return items;
        }

        var anchos = new Dictionary<DataGridColumn, double>();
        var pisos = new Dictionary<DataGridColumn, double>();
        var sinRealizar = false;
        var noMedibles = false;
        foreach (var c in marcadas)
        {
            var medida = ColumnaAuto.Medir(grid, c, Filas, out var nuncaMedible);
            if (medida is null)
            {
                if (nuncaMedible)
                    noMedibles = true;
                else
                    sinRealizar = true;
                continue;
            }
            anchos[c] = Math.Max(medida.Value.Ancho, estado.AnchosMedidos.GetValueOrDefault(c));
            pisos[c] = Math.Min(anchos[c], medida.Value.Piso);
            estado.MedidasDesdeElCambio.Add(c);
        }

        if (noMedibles)
            estado.IntentosDeMedicion++;
        estado.MedicionPendiente = sinRealizar || (noMedibles && estado.IntentosDeMedicion < MaxIntentosDeMedicion);
        estado.Reparando = true;
        try
        {
            foreach (var (c, ancho) in anchos)
            {
                estado.AnchosMedidos[c] = ancho;
                estado.PisosMedidos[c] = pisos[c];
                if (!c.Width.IsAbsolute || Math.Abs(c.Width.Value - ancho) > Tolerancia)
                    Asignar(c, new DataGridLength(ancho, DataGridLengthUnitType.Pixel));
            }
        }
        finally
        {
            estado.Reparando = false;
        }
    }

    // ── Reparto ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Espejo de <c>DataGridColumn.ActualMinWidth</c> (interno). Para una columna <c>*</c> es su
    /// piso: el máximo entre ese mínimo y <see cref="PisoProporcionalProperty"/>.
    /// </summary>
    private static double Minimo(DataGrid grid, DataGridColumn c, bool esEstrella)
    {
        var minimo = c.MinWidth > 0 ? c.MinWidth : grid.MinColumnWidth;
        if (!esEstrella)
            return minimo;
        var piso = Math.Max(minimo, GetPisoProporcional(c));
        return Math.Max(0.001, Math.Min(c.MaxWidth, piso));
    }

    /// <summary>El ancho que tiene que tener una columna no proporcional, o null si no aplica.</summary>
    private static double? Objetivo(DataGrid grid, DataGridColumn c)
    {
        var w = c.Width;
        double objetivo;
        if (w.IsAbsolute)
            objetivo = w.Value;
        else if (w.IsAuto || w.IsSizeToCells || w.IsSizeToHeader)
            objetivo = w.DesiredValue;
        else
            return null;

        if (double.IsNaN(objetivo))
            return null;
        return Math.Min(c.MaxWidth, Math.Max(Minimo(grid, c, esEstrella: false), objetivo));
    }

    /// <summary>
    /// <c>DataGrid.CellsWidth</c> (interno en DataGrid 12.0.1): el ancho del último measure del
    /// presentador de filas, que es contra lo que la propia grilla estira las <c>*</c> y decide
    /// el scroll horizontal. Null si una versión futura lo renombra (ver
    /// <see cref="AnchoDisponible"/>).
    /// </summary>
    private static readonly PropertyInfo? CellsWidth =
        typeof(DataGrid).GetProperty("CellsWidth", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>Si el behavior puede leer el ancho disponible de la grilla (guardián de actualizaciones).</summary>
    internal static bool PuedeLeerCellsWidth => CellsWidth?.PropertyType == typeof(double);

    /// <summary>
    /// Ancho disponible para las columnas: el <c>CellsWidth</c> interno de la grilla.
    ///
    /// <para><b>Anti-oscilación en la transición encaja/desborde.</b> La decisión tiene que
    /// medir lo MISMO en los dos modos. Antes, en desborde, se usaba el ancho del presentador de
    /// filas, pero en el <c>LayoutUpdated</c> que sigue a un achique de ventana ese
    /// <c>Bounds</c> todavía es el viejo mientras que el measure ya bajó <c>CellsWidth</c>
    /// (medido: ventana de 1774 a 1764, presentador 1458 y CellsWidth 1448). Justo en el umbral eso daba "desborde" medido con el ancho nuevo y
    /// "encaja" medido con el viejo, alternando hasta agotar
    /// <see cref="MaxReparacionesSeguidas"/> y dejando las * bajo el piso. <c>CellsWidth</c> es
    /// el mismo número en los dos modos (con las * como *, la grilla las estira para que la suma
    /// sea exactamente ese ancho).</para>
    ///
    /// <para>Si una versión futura de DataGrid no expone <c>CellsWidth</c>, se vuelve a la
    /// aproximación anterior: la suma de columnas con las * como *, o el presentador de filas en
    /// desborde (con el riesgo de oscilación acotado por la guarda anti-loop).</para>
    /// </summary>
    private static double? AnchoDisponible(DataGrid grid, Estado estado, List<DataGridColumn> visibles, List<DataGridColumn> estrellas)
    {
        if (PuedeLeerCellsWidth && CellsWidth!.GetValue(grid) is double cells)
            return cells > 0 && !double.IsInfinity(cells) ? cells : null;

        if (estrellas.All(c => c.Width.IsStar))
            return visibles.Sum(c => c.ActualWidth);

        if (estado.Filas is null || !estado.Filas.TryGetTarget(out var filas) || filas.FindAncestorOfType<DataGrid>() != grid)
        {
            filas = grid.FindDescendantOfType<DataGridRowsPresenter>();
            if (filas is null)
                return null;
            estado.Filas = new WeakReference<DataGridRowsPresenter>(filas);
        }
        return filas.Bounds.Width > 0 ? filas.Bounds.Width : null;
    }

    /// <summary>Reparto de <paramref name="resto"/> entre las * según su peso, sin bajar de su mínimo.</summary>
    private static Dictionary<DataGridColumn, double> RepartirEstrellas(
        DataGrid grid, List<DataGridColumn> estrellas, Dictionary<DataGridColumn, double> pesos, double resto)
    {
        var anchos = new Dictionary<DataGridColumn, double>();
        var libres = estrellas.ToList();
        while (true)
        {
            var pesoLibre = libres.Sum(c => pesos[c]);
            var restoLibre = resto - anchos.Values.Sum();
            var topeadas = libres
                .Where(c => restoLibre * pesos[c] / pesoLibre < Minimo(grid, c, esEstrella: true))
                .ToList();
            if (topeadas.Count == 0)
            {
                foreach (var c in libres)
                    anchos[c] = restoLibre * pesos[c] / pesoLibre;
                return anchos;
            }
            foreach (var c in topeadas)
            {
                anchos[c] = Minimo(grid, c, esEstrella: true);
                libres.Remove(c);
            }
            if (libres.Count == 0)
                return anchos;
        }
    }

    /// <summary>Las columnas medidas que el reparto maneja: en píxeles por la medición y no proporcionales.</summary>
    private static List<DataGridColumn> Medidas(Estado estado, List<DataGridColumn> visibles, List<DataGridColumn> estrellas) =>
        visibles.Where(c => !estrellas.Contains(c) && c.Width.IsAbsolute && estado.AnchosMedidos.ContainsKey(c)).ToList();

    /// <summary>
    /// Achica las columnas medidas de <paramref name="objetivos"/> para que entren en <paramref name="lugar"/> (el
    /// ancho de la grilla menos las demás no proporcionales y el piso de las *): las nivela hacia abajo, de la más
    /// ancha a la más angosta, sin bajar ninguna de su piso. El nivel es el mayor entero que entra, así el
    /// píxel fraccionario que sobra va a las *. Si con todas en su piso tampoco entra, quedan en el piso.
    /// </summary>
    private static void CederMedidas(Estado estado, Dictionary<DataGridColumn, double> objetivos, List<DataGridColumn> medidas, double lugar)
    {
        if (medidas.Count == 0 || medidas.Sum(c => objetivos[c]) <= lugar + Tolerancia)
            return;

        var pedidos = medidas.ToDictionary(c => c, c => objetivos[c]);
        var pisos = medidas.ToDictionary(c => c, c => Math.Min(pedidos[c], estado.PisosMedidos.GetValueOrDefault(c, pedidos[c])));
        double Total(double nivel) => medidas.Sum(c => Math.Max(pisos[c], Math.Min(pedidos[c], nivel)));

        // Total crece con el nivel: el mayor nivel entero que entra, por bisección.
        var bajo = 0.0;
        var alto = Math.Ceiling(pedidos.Values.Max());
        while (alto - bajo > 1)
        {
            var medio = Math.Floor((bajo + alto) / 2);
            if (Total(medio) <= lugar + Tolerancia)
                bajo = medio;
            else
                alto = medio;
        }
        foreach (var c in medidas)
            objetivos[c] = Math.Max(pisos[c], Math.Min(pedidos[c], bajo));
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is not DataGrid grid || !Estados.TryGetValue(grid, out var estado))
            return;
        if (estado.Reparando || estado.UsuarioRedimensiono || estado.AnchosAlApretar is not null)
            return;
        if (!grid.IsEffectivelyVisible || (grid.HeadersVisibility & DataGridHeadersVisibility.Row) != 0)
            return;

        if (estado.MedicionPendiente)
            MedirColumnas(grid, estado);

        var visibles = grid.Columns.Where(c => c.IsVisible).ToList();
        foreach (var c in visibles.Where(c => c.Width.IsStar && !estado.PesosDeclarados.ContainsKey(c)))
            estado.PesosDeclarados[c] = c.Width.Value;

        var estrellas = visibles.Where(c => estado.PesosDeclarados.ContainsKey(c) && estado.PesosDeclarados[c] > 0).ToList();
        var medidas = Medidas(estado, visibles, estrellas);
        // Sin columnas * no hay camino de Resize limitado: el crecimiento Auto ya funciona solo. Solo queda que
        // las columnas medidas cedan si no entran.
        if (estrellas.Count == 0 && medidas.Count == 0)
            return;

        var disponible = AnchoDisponible(grid, estado, visibles, estrellas);
        if (disponible is null)
            return;

        var objetivos = new Dictionary<DataGridColumn, double>();
        foreach (var c in visibles.Where(c => !estrellas.Contains(c)))
        {
            var objetivo = medidas.Contains(c) ? Math.Min(c.MaxWidth, estado.AnchosMedidos[c]) : Objetivo(grid, c);
            if (objetivo is null)
                return; // Todavía sin medir: la grilla no terminó su primera pasada.
            objetivos[c] = objetivo.Value;
        }

        var minimosEstrellas = estrellas.Sum(c => Minimo(grid, c, esEstrella: true));
        var otras = objetivos.Where(kv => !medidas.Contains(kv.Key)).Sum(kv => kv.Value);
        CederMedidas(estado, objetivos, medidas, disponible.Value - otras - minimosEstrellas);
        var encaja = objetivos.Values.Sum() + minimosEstrellas <= disponible.Value + Tolerancia;

        var anchosEstrellas = encaja
            ? RepartirEstrellas(grid, estrellas, estado.PesosDeclarados, disponible.Value - objetivos.Values.Sum())
            : estrellas.ToDictionary(c => c, c => Minimo(grid, c, esEstrella: true));
        // Sin * el resto de las columnas sigue como la grilla las deja: solo se asignan las medidas.
        if (estrellas.Count == 0)
            foreach (var c in objetivos.Keys.Except(medidas).ToList())
                objetivos.Remove(c);

        var enEquilibrio =
            objetivos.All(kv => Math.Abs(kv.Key.ActualWidth - kv.Value) <= Tolerancia
                && (!medidas.Contains(kv.Key) || Math.Abs(kv.Key.Width.Value - kv.Value) <= Tolerancia))
            && estrellas.All(c =>
                Math.Abs(c.ActualWidth - anchosEstrellas[c]) <= Tolerancia
                && (encaja
                    ? c.Width.IsStar && Math.Abs(c.Width.Value - estado.PesosDeclarados[c]) < 0.001
                    : c.Width.IsAbsolute));
        if (enEquilibrio)
        {
            estado.ReparacionesSeguidas = 0;
            return;
        }

        // Guarda anti-loop.
        if (++estado.ReparacionesSeguidas > MaxReparacionesSeguidas)
            return;

        estado.Reparando = true;
        try
        {
            foreach (var (c, objetivo) in objetivos)
            {
                var w = c.Width;
                Asignar(c, medidas.Contains(c)
                    ? new DataGridLength(objetivo, DataGridLengthUnitType.Pixel, objetivo, objetivo)
                    : new DataGridLength(w.Value, w.UnitType, objetivo, objetivo));
            }
            foreach (var c in estrellas)
            {
                var ancho = anchosEstrellas[c];
                Asignar(c, encaja
                    ? new DataGridLength(estado.PesosDeclarados[c], DataGridLengthUnitType.Star, ancho, ancho)
                    : new DataGridLength(ancho, DataGridLengthUnitType.Pixel));
            }
        }
        finally
        {
            estado.Reparando = false;
        }
    }

    /// <summary>
    /// Asigna el ancho sin pasar por <c>DataGridColumn.Resize</c>: un cambio de <c>Width</c> que
    /// cambia <c>IsStar</c> se aplica tal cual (DesiredValue y DisplayValue incluidos). Si el
    /// ancho actual y el nuevo son del mismo tipo, se pasa por uno intermedio del tipo contrario;
    /// el intermedio no llega a medirse porque todo ocurre antes de la pasada de layout.
    /// </summary>
    private static void Asignar(DataGridColumn columna, DataGridLength ancho)
    {
        if (columna.Width.IsStar == ancho.IsStar)
            columna.Width = ancho.IsStar ? DataGridLength.Auto : new DataGridLength(1, DataGridLengthUnitType.Star);
        columna.Width = ancho;
    }
}
