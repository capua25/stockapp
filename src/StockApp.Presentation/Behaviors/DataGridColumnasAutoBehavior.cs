using System;
using System.Collections.Generic;
using System.Linq;
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
/// <item>Si no entra ("modo desborde"), las <c>*</c> pasan a ancho fijo en su mínimo, que es
/// donde las deja la grilla sola en ese caso. Sin columnas <c>*</c>, la grilla deja de apretar y
/// lo que no entra se ve con el scroll horizontal. Cuando vuelve a entrar, recuperan su peso.</item>
/// </list>
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
/// </list>
/// </summary>
public static class DataGridColumnasAutoBehavior
{
    public static readonly AttachedProperty<bool> AjustarColumnasAutoProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, DataGrid, bool>("AjustarColumnasAuto");

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
    }

    private static readonly ConditionalWeakTable<DataGrid, Estado> Estados = new();

    static DataGridColumnasAutoBehavior()
    {
        AjustarColumnasAutoProperty.Changed.AddClassHandler<DataGrid>(OnAjustarColumnasAutoChanged);
    }

    public static bool GetAjustarColumnasAuto(DataGrid grid) => grid.GetValue(AjustarColumnasAutoProperty);

    public static void SetAjustarColumnasAuto(DataGrid grid, bool value) => grid.SetValue(AjustarColumnasAutoProperty, value);

    private static void OnAjustarColumnasAutoChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs e)
    {
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

    // ── Reparto ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Espejo de <c>DataGridColumn.ActualMinWidth</c> (interno).</summary>
    private static double Minimo(DataGrid grid, DataGridColumn c, bool esEstrella)
    {
        var minimo = c.MinWidth > 0 ? c.MinWidth : grid.MinColumnWidth;
        return esEstrella ? Math.Max(0.001, minimo) : minimo;
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
    /// Ancho disponible para las columnas (el <c>CellsWidth</c> interno de la grilla).
    /// Mientras las * siguen siendo *, la grilla las estira para que la suma de columnas sea
    /// EXACTAMENTE ese ancho: se usa esa suma, así el reparto calculado coincide al píxel con el
    /// de la grilla y no hay nada que pelear (con escalado de pantalla, el ancho del presentador
    /// de filas puede diferir por redondeo). En modo desborde (las * pasadas a ancho fijo) la
    /// suma ya no dice nada y se usa el ancho del presentador de filas, solo para decidir si se
    /// vuelve a "encaja".
    /// </summary>
    private static double? AnchoDisponible(DataGrid grid, Estado estado, List<DataGridColumn> visibles, List<DataGridColumn> estrellas)
    {
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

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is not DataGrid grid || !Estados.TryGetValue(grid, out var estado))
            return;
        if (estado.Reparando || estado.UsuarioRedimensiono || estado.AnchosAlApretar is not null)
            return;
        if (!grid.IsEffectivelyVisible || (grid.HeadersVisibility & DataGridHeadersVisibility.Row) != 0)
            return;

        var visibles = grid.Columns.Where(c => c.IsVisible).ToList();
        foreach (var c in visibles.Where(c => c.Width.IsStar && !estado.PesosDeclarados.ContainsKey(c)))
            estado.PesosDeclarados[c] = c.Width.Value;

        var estrellas = visibles.Where(c => estado.PesosDeclarados.ContainsKey(c) && estado.PesosDeclarados[c] > 0).ToList();
        // Sin columnas * no hay camino de Resize limitado: el crecimiento Auto ya funciona solo.
        if (estrellas.Count == 0)
            return;

        var disponible = AnchoDisponible(grid, estado, visibles, estrellas);
        if (disponible is null)
            return;

        var objetivos = new Dictionary<DataGridColumn, double>();
        foreach (var c in visibles.Where(c => !estrellas.Contains(c)))
        {
            var objetivo = Objetivo(grid, c);
            if (objetivo is null)
                return; // Todavía sin medir: la grilla no terminó su primera pasada.
            objetivos[c] = objetivo.Value;
        }

        var minimosEstrellas = estrellas.Sum(c => Minimo(grid, c, esEstrella: true));
        var encaja = objetivos.Values.Sum() + minimosEstrellas <= disponible.Value + Tolerancia;

        var anchosEstrellas = encaja
            ? RepartirEstrellas(grid, estrellas, estado.PesosDeclarados, disponible.Value - objetivos.Values.Sum())
            : estrellas.ToDictionary(c => c, c => Minimo(grid, c, esEstrella: true));

        var enEquilibrio =
            objetivos.All(kv => Math.Abs(kv.Key.ActualWidth - kv.Value) <= Tolerancia)
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
                Asignar(c, new DataGridLength(w.Value, w.UnitType, objetivo, objetivo));
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
