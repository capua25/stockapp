using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace StockApp.Presentation.Views.Reportes;

/// <summary>
/// Marco común de los seis reportes. Ranuras: <see cref="Acciones"/> (encabezado), <see cref="Filtros"/> (barra de filtros;
/// null = sin barra), <see cref="Contenido"/> (la grilla) y <see cref="Pie"/>. Los estados de carga, sin conexión y sin permiso
/// salen del ViewModel del reporte (DataContext); la vista informa <see cref="HayDatos"/> y los textos del vacío.
/// </summary>
public partial class MarcoReporte : UserControl
{
    public static readonly StyledProperty<string?> TituloProperty = AvaloniaProperty.Register<MarcoReporte, string?>(nameof(Titulo));
    public static readonly StyledProperty<ICommand?> VolverCommandProperty = AvaloniaProperty.Register<MarcoReporte, ICommand?>(nameof(VolverCommand));
    public static readonly StyledProperty<object?> AccionesProperty = AvaloniaProperty.Register<MarcoReporte, object?>(nameof(Acciones));
    public static readonly StyledProperty<object?> FiltrosProperty = AvaloniaProperty.Register<MarcoReporte, object?>(nameof(Filtros));
    public static readonly StyledProperty<string?> MensajeErrorProperty = AvaloniaProperty.Register<MarcoReporte, string?>(nameof(MensajeError));
    public static readonly StyledProperty<object?> ContenidoProperty = AvaloniaProperty.Register<MarcoReporte, object?>(nameof(Contenido));
    public static readonly StyledProperty<object?> PieProperty = AvaloniaProperty.Register<MarcoReporte, object?>(nameof(Pie));
    public static readonly StyledProperty<bool> HayDatosProperty = AvaloniaProperty.Register<MarcoReporte, bool>(nameof(HayDatos));
    public static readonly StyledProperty<string?> TituloVacioProperty = AvaloniaProperty.Register<MarcoReporte, string?>(nameof(TituloVacio));
    public static readonly StyledProperty<string?> MensajeVacioProperty = AvaloniaProperty.Register<MarcoReporte, string?>(nameof(MensajeVacio));

    public MarcoReporte() => InitializeComponent();

    public string? Titulo { get => GetValue(TituloProperty); set => SetValue(TituloProperty, value); }
    public ICommand? VolverCommand { get => GetValue(VolverCommandProperty); set => SetValue(VolverCommandProperty, value); }
    public object? Acciones { get => GetValue(AccionesProperty); set => SetValue(AccionesProperty, value); }
    public object? Filtros { get => GetValue(FiltrosProperty); set => SetValue(FiltrosProperty, value); }
    public string? MensajeError { get => GetValue(MensajeErrorProperty); set => SetValue(MensajeErrorProperty, value); }
    public object? Contenido { get => GetValue(ContenidoProperty); set => SetValue(ContenidoProperty, value); }
    public object? Pie { get => GetValue(PieProperty); set => SetValue(PieProperty, value); }
    public bool HayDatos { get => GetValue(HayDatosProperty); set => SetValue(HayDatosProperty, value); }
    public string? TituloVacio { get => GetValue(TituloVacioProperty); set => SetValue(TituloVacioProperty, value); }
    public string? MensajeVacio { get => GetValue(MensajeVacioProperty); set => SetValue(MensajeVacioProperty, value); }
}
