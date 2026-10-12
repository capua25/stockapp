using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Barra fina de ejecución presupuestal (Control POA): pista y relleno redondeados, el relleno proporcional al porcentaje
/// con tope en 100 y en tono de peligro por encima. Se dibuja a mano (dos rectángulos) para fijarla contra el mockup:
/// el ProgressBar de Fluent trae alturas mínimas y animación.
/// </summary>
public class BarraEjecucion : Control
{
    public static readonly StyledProperty<decimal> PorcentajeProperty =
        AvaloniaProperty.Register<BarraEjecucion, decimal>(nameof(Porcentaje));

    public static readonly StyledProperty<IBrush?> PistaProperty =
        AvaloniaProperty.Register<BarraEjecucion, IBrush?>(nameof(Pista));

    public static readonly StyledProperty<IBrush?> RellenoProperty =
        AvaloniaProperty.Register<BarraEjecucion, IBrush?>(nameof(Relleno));

    public static readonly StyledProperty<IBrush?> RellenoExcedidoProperty =
        AvaloniaProperty.Register<BarraEjecucion, IBrush?>(nameof(RellenoExcedido));

    static BarraEjecucion()
    {
        AffectsRender<BarraEjecucion>(PorcentajeProperty, PistaProperty, RellenoProperty, RellenoExcedidoProperty);
    }

    public decimal Porcentaje { get => GetValue(PorcentajeProperty); set => SetValue(PorcentajeProperty, value); }
    public IBrush? Pista { get => GetValue(PistaProperty); set => SetValue(PistaProperty, value); }
    public IBrush? Relleno { get => GetValue(RellenoProperty); set => SetValue(RellenoProperty, value); }
    public IBrush? RellenoExcedido { get => GetValue(RellenoExcedidoProperty); set => SetValue(RellenoExcedidoProperty, value); }

    /// <summary>El relleno que se pinta: el de peligro cuando la línea gastó más que su presupuesto.</summary>
    public IBrush? RellenoVigente => Porcentaje > 100m ? RellenoExcedido : Relleno;

    /// <summary>Ancho del relleno en píxeles enteros (el mockup escribe el mismo redondeo).</summary>
    public double AnchoRelleno => Math.Round(Bounds.Width * (double)Math.Clamp(Porcentaje, 0m, 100m) / 100d, MidpointRounding.AwayFromZero);

    public override void Render(DrawingContext contexto)
    {
        var alto = Bounds.Height;
        var radio = alto / 2;
        if (Pista is { } pista)
            contexto.DrawRectangle(pista, null, new Rect(0, 0, Bounds.Width, alto), radio, radio);
        if (AnchoRelleno > 0 && RellenoVigente is { } relleno)
            contexto.DrawRectangle(relleno, null, new Rect(0, 0, AnchoRelleno, alto), radio, radio);
    }
}
