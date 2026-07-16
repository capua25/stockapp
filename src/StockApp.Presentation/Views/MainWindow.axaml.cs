using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.Views;

public partial class MainWindow : Window
{
    private readonly IServicioEstadoVentana? _servicioEstadoVentana;

    // Último tamaño/posición "normal" (no maximizada) conocidos. Se actualizan cada vez que
    // la ventana vuelve a WindowState.Normal, para poder guardar coordenadas coherentes aun
    // si se cierra estando maximizada (ver OnClosing).
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

        PropertyChanged += OnPropertyChanged;
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

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty && WindowState == WindowState.Normal)
        {
            _ultimaPosicionNormal = Position;
            _ultimoTamanioNormal = new Size(Width, Height);
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_servicioEstadoVentana is null)
            return;

        // Si se cierra maximizada, se guarda el último tamaño/posición "normal" conocido
        // (no el de maximizada) junto con Maximizada=true, para poder des-maximizar
        // coherentemente la próxima vez.
        var estado = new EstadoVentana
        {
            Ancho = _ultimoTamanioNormal.Width,
            Alto = _ultimoTamanioNormal.Height,
            X = _ultimaPosicionNormal.X,
            Y = _ultimaPosicionNormal.Y,
            Maximizada = WindowState == WindowState.Maximized,
        };

        _servicioEstadoVentana.Guardar(estado);
    }
}
