using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.Views;

public partial class MainWindow : Window
{
    private readonly IServicioEstadoVentana? _servicioEstadoVentana;

    // Último tamaño/posición "normal" (no maximizada) conocidos. Se actualizan en cada
    // Resized/PositionChanged mientras la ventana está efectivamente en WindowState.Normal,
    // para poder guardar coordenadas coherentes si se cierra estando maximizada (ver
    // OnClosing). No se capturan en la transición de WindowState: al restaurar desde
    // maximizada, Position/Bounds pueden todavía reflejar momentáneamente los valores
    // maximizados (ver Avalonia issue #5285 / discussion #7836), así que la fuente de verdad
    // para el caso maximizado son estos eventos, no el handler de WindowStateProperty.
    private PixelPoint _ultimaPosicionNormal;
    private Size _ultimoTamanioNormal;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(IServicioEstadoVentana? servicioEstadoVentana)
    {
        InitializeComponent();

        _servicioEstadoVentana = servicioEstadoVentana;
        _ultimaPosicionNormal = Position;
        _ultimoTamanioNormal = new Size(Width, Height);

        AplicarEstadoGuardado();

        Resized += OnResizedOrPositionChanged;
        PositionChanged += OnResizedOrPositionChanged;
        Closing += OnClosing;
    }

    /// <summary>
    /// Aplica el último estado guardado (tamaño, posición, maximizada) si existe y sigue
    /// siendo válido para la configuración de pantallas actual. Si no hay estado guardado,
    /// si está corrupto, o si quedó fuera de toda pantalla visible (ej. monitor
    /// desenchufado), la ventana abre con sus defaults del XAML.
    /// </summary>
    private void AplicarEstadoGuardado()
    {
        var estado = _servicioEstadoVentana?.Cargar();
        if (estado is null)
            return;

        try
        {
            var pantallas = Screens.All.Select(s => s.Bounds);
            if (!ValidadorEstadoVentana.EsVisibleEn(estado, pantallas))
                return;
        }
        catch
        {
            // Defensivo: si por lo que sea no se puede consultar Screens en este punto del
            // ciclo de vida, no aplicamos la posición guardada y seguimos con los defaults.
            return;
        }

        Width = estado.Ancho;
        Height = estado.Alto;
        Position = new PixelPoint(estado.X, estado.Y);

        _ultimaPosicionNormal = Position;
        _ultimoTamanioNormal = new Size(Width, Height);

        if (estado.Maximizada)
        {
            WindowState = WindowState.Maximized;
        }
    }

    // Solo alimenta el tracker _ultimaPosicionNormal/_ultimoTamanioNormal, que es la fuente
    // usada por OnClosing exclusivamente para el caso "se cierra maximizada" (ver ahí). Se
    // ignora cualquier evento que no ocurra con la ventana efectivamente en Normal: en la
    // transición de estado (restaurar desde maximizada), Position/Bounds puede todavía
    // reflejar momentáneamente los valores maximizados.
    private void OnResizedOrPositionChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Normal)
            return;

        _ultimaPosicionNormal = Position;
        _ultimoTamanioNormal = new Size(Width, Height);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_servicioEstadoVentana is null)
            return;

        // Si se cierra maximizada, no hay tamaño/posición "normal" actual que leer (Width/
        // Height/Position reflejan la maximizada), así que se usa el último conocido vía
        // el tracker. Si NO está maximizada, se usan los valores actuales directamente: el
        // tracker puede haber quedado desactualizado si la ventana se movió/redimensionó
        // sin pasar por una transición de WindowState intermedia detectable a tiempo.
        double ancho;
        double alto;
        PixelPoint posicion;

        if (WindowState == WindowState.Maximized)
        {
            ancho = _ultimoTamanioNormal.Width;
            alto = _ultimoTamanioNormal.Height;
            posicion = _ultimaPosicionNormal;
        }
        else
        {
            ancho = Width;
            alto = Height;
            posicion = Position;
        }

        var estado = new EstadoVentana
        {
            Ancho = ancho,
            Alto = alto,
            X = posicion.X,
            Y = posicion.Y,
            Maximizada = WindowState == WindowState.Maximized,
        };

        _servicioEstadoVentana.Guardar(estado);
    }
}
