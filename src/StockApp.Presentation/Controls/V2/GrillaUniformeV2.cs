using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// N columnas iguales separadas por <see cref="Espaciado"/> (fila de métricas de Inicio, accesos rápidos,
/// tarjetas del hub de Reportes). Cada borde de celda se redondea por separado; UniformGrid redondea el
/// ancho y acumula el error (Fase 2, decisiones de diseño). Espejo CSS: .v2-grilla-uniforme más
/// .v2-grilla-uniforme--2 / --3 de base.css (columnas de ancho entero round(nearest, k * celda, 1px)
/// menos round(nearest, (k - 1) * celda, 1px), la del medio minmax(0, 1fr)), con --v2-grilla-espaciado
/// igual a <see cref="Espaciado"/>. repeat(N, minmax(0, 1fr)) NO es el espejo: deja los bordes interiores
/// en x fraccionaria (spec §6.5, compensación B). Los hijos invisibles no ocupan columna. Cada fila mide
/// lo que su ítem más alto, como grid-auto-rows: auto.
/// </summary>
public class GrillaUniformeV2 : Panel
{
    public static readonly StyledProperty<int> ColumnasProperty =
        AvaloniaProperty.Register<GrillaUniformeV2, int>(nameof(Columnas));

    public static readonly StyledProperty<double> EspaciadoProperty =
        AvaloniaProperty.Register<GrillaUniformeV2, double>(nameof(Espaciado));

    static GrillaUniformeV2()
    {
        AffectsMeasure<GrillaUniformeV2>(ColumnasProperty, EspaciadoProperty);
    }

    /// <summary>Cantidad de columnas; 0 = tantas como hijos visibles (una sola fila).</summary>
    public int Columnas
    {
        get => GetValue(ColumnasProperty);
        set => SetValue(ColumnasProperty, value);
    }

    public double Espaciado
    {
        get => GetValue(EspaciadoProperty);
        set => SetValue(EspaciadoProperty, value);
    }

    private List<Control> Visibles() => Children.Where(c => c.IsVisible).ToList();

    private int ColumnasEfectivas(int visibles) => Columnas > 0 ? Columnas : Math.Max(visibles, 1);

    protected override Size MeasureOverride(Size disponible)
    {
        var hijos = Visibles();
        if (hijos.Count == 0)
        {
            return default;
        }

        var n = ColumnasEfectivas(hijos.Count);
        var anchoCelda = double.IsInfinity(disponible.Width)
            ? double.PositiveInfinity
            : Math.Max(0, (disponible.Width - (n - 1) * Espaciado) / n);
        double alto = 0, anchoMaximo = 0;
        foreach (var fila in hijos.Chunk(n))
        {
            double altoFila = 0;
            foreach (var hijo in fila)
            {
                hijo.Measure(new Size(anchoCelda, double.PositiveInfinity));
                altoFila = Math.Max(altoFila, hijo.DesiredSize.Height);
                anchoMaximo = Math.Max(anchoMaximo, hijo.DesiredSize.Width);
            }

            alto += altoFila;
        }

        var filas = (hijos.Count + n - 1) / n;
        alto += (filas - 1) * Espaciado;
        var ancho = double.IsInfinity(disponible.Width) ? anchoMaximo * n + (n - 1) * Espaciado : disponible.Width;
        return new Size(ancho, alto);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var hijos = Visibles();
        if (hijos.Count == 0)
        {
            return final;
        }

        var n = ColumnasEfectivas(hijos.Count);
        var celda = Math.Max(0, (final.Width - (n - 1) * Espaciado) / n);
        double y = 0;
        foreach (var fila in hijos.Chunk(n))
        {
            var altoFila = fila.Max(h => h.DesiredSize.Height);
            for (var i = 0; i < fila.Length; i++)
            {
                // Bordes redondeados por separado (pixel snapping de Chromium): izquierda y derecha de cada
                // celda caen donde las pone el mockup, sin acumular el redondeo del ancho.
                var izquierda = Math.Round(i * (celda + Espaciado), MidpointRounding.AwayFromZero);
                var derecha = Math.Round(i * (celda + Espaciado) + celda, MidpointRounding.AwayFromZero);
                fila[i].Arrange(new Rect(izquierda, y, derecha - izquierda, altoFila));
            }

            y += altoFila + Espaciado;
        }

        return final;
    }
}
