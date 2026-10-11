using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.Views.Dialogs;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Confirmación de cambios de precio de costo (convención 25): el diálogo real, sin VM. Devuelve los tildes en el orden
/// de la lista, o null si se cancela.
/// </summary>
public class ConfirmacionPreciosDialogTests
{
    private static ItemConfirmacionPrecioVm Cambio(string nombre, decimal actual, decimal nuevo, bool tildado = false) =>
        new() { Fila = new FilaRenglonFacturaVm(), ProductoNombre = nombre, PrecioActual = actual, PrecioNuevo = nuevo, Confirmado = tildado };

    private static IReadOnlyList<ItemConfirmacionPrecioVm> Tres() =>
    [
        Cambio("Cemento portland 50 kg", 480m, 510m, tildado: true),
        Cambio("Arena gruesa", 890m, 920m),
        Cambio("Cable 2x1,5 mm", 38m, 41.5m),
    ];

    private static (ConfirmacionPreciosDialog Dialogo, Task<IReadOnlyList<bool>?> Resultado) Abrir(IReadOnlyList<ItemConfirmacionPrecioVm>? cambios = null)
    {
        var dialogo = new ConfirmacionPreciosDialog(cambios ?? Tres());
        var duenio = new Window();
        duenio.Show();
        var resultado = dialogo.ShowDialog<IReadOnlyList<bool>?>(duenio);
        Dispatcher.UIThread.RunJobs();
        return (dialogo, resultado);
    }

    private static T Parte<T>(Window d, string nombre) where T : Control => d.FindControl<T>(nombre)!;

    private static List<CheckBox> Tildes(Window d) => Parte<Grid>(d, "Filas").Children.OfType<CheckBox>().ToList();

    private static void Clic(Window d, string boton)
    {
        Parte<Button>(d, boton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Tecla(Window d, Control foco, PhysicalKey tecla)
    {
        foco.Focus();
        Dispatcher.UIThread.RunJobs();
        d.KeyPressQwerty(tecla, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void MuestraCadaCambioConPreciosEnEsUy_ConCulturaAmbienteEnUs()
    {
        var anterior = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var (d, _) = Abrir([Cambio("Pala punta cuadrada", 1500.5m, 2000m)]);

            var textos = d.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Pala punta cuadrada", textos);
            Assert.Contains("$ 1.500,50", textos);
            Assert.Contains("$ 2.000,00", textos);
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }

    [AvaloniaFact]
    public void LosTildesArrancanComoVienen() =>
        Assert.Equal(new bool?[] { true, false, false }, Tildes(Abrir().Dialogo).Select(t => t.IsChecked));

    [AvaloniaFact]
    public void Confirmar_DevuelveLosTildesEnOrden()
    {
        var (d, resultado) = Abrir();
        Tildes(d)[2].IsChecked = true;

        Clic(d, "ConfirmarButton");

        Assert.True(resultado.IsCompleted);
        Assert.Equal(new[] { true, false, true }, resultado.Result);
    }

    [AvaloniaFact]
    public void Cancelar_DevuelveNull()
    {
        var (d, resultado) = Abrir();

        Clic(d, "CancelarButton");

        Assert.True(resultado.IsCompleted);
        Assert.Null(resultado.Result);
    }

    [AvaloniaFact]
    public void Escape_Cancela()
    {
        var (d, resultado) = Abrir();

        Tecla(d, Tildes(d)[1], PhysicalKey.Escape);

        Assert.True(resultado.IsCompleted);
        Assert.Null(resultado.Result);
    }

    [AvaloniaFact]
    public void Enter_ConElFocoEnUnTilde_ConfirmaSinCambiarlo()
    {
        var (d, resultado) = Abrir();

        Tecla(d, Tildes(d)[1], PhysicalKey.Enter);

        Assert.True(resultado.IsCompleted, "Enter sobre un tilde lo cambió y no confirmó.");
        Assert.Equal(new[] { true, false, false }, resultado.Result);
    }

    [AvaloniaFact]
    public void Abre_ConElFocoEnConfirmarYGuardar_YEnterConfirma()
    {
        var (d, resultado) = Abrir();

        Assert.Same(Parte<Button>(d, "ConfirmarButton"), d.FocusManager!.GetFocusedElement());
        d.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(resultado.IsCompleted);
    }

    [AvaloniaFact]
    public void Estructura_Ui2_UnSoloPrimario_YCancelarConfirmarConTeclado()
    {
        var d = new ConfirmacionPreciosDialog(Tres());

        var contenido = Assert.IsType<Border>(d.Content);
        Assert.Equal("Contenido", contenido.Name);
        Assert.Contains("ui2", contenido.Classes);
        Assert.Equal("Cambios de precio de costo", d.Title);
        Assert.Equal(520, d.Width);
        Assert.Equal(SizeToContent.Height, d.SizeToContent);
        Assert.Equal(WindowStartupLocation.CenterOwner, d.WindowStartupLocation);
        Assert.False(d.CanResize);
        Assert.False(d.ShowInTaskbar);
        var primarios = contenido.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primario")).ToList();
        Assert.Same(Parte<Button>(d, "ConfirmarButton"), Assert.Single(primarios));
        Assert.True(Parte<Button>(d, "CancelarButton").IsCancel);
        Assert.True(Parte<Button>(d, "ConfirmarButton").IsDefault);
        Assert.Equal("Confirmar y guardar", Parte<Button>(d, "ConfirmarButton").Content);
    }

    [AvaloniaFact]
    public void VeinticincoCambios_LaListaScrollea_YLosBotonesSiguenDentroDelDialogo()
    {
        var muchos = Enumerable.Range(1, 25).Select(i => Cambio($"Producto de prueba {i}", 100m, 100m + i)).ToList();
        var (d, _) = Abrir(muchos);

        var lista = Parte<ScrollViewer>(d, "Lista");
        Assert.True(lista.Extent.Height > lista.Viewport.Height + 0.5, "La lista no scrollea: el diálogo crece sin tope.");
        Assert.True(lista.Bounds.Height <= 320 + 0.5);
        var confirmar = Parte<Button>(d, "ConfirmarButton");
        var abajo = confirmar.TranslatePoint(new Point(0, confirmar.Bounds.Height), d)!.Value.Y;
        Assert.True(abajo <= d.Bounds.Height + 0.5, $"Confirmar termina en {abajo:F1} y el diálogo mide {d.Bounds.Height:F1}.");
    }

    [AvaloniaFact]
    public void NombreLargo_EnvuelveYNoSeSaleDelAncho()
    {
        var (d, _) = Abrir([Cambio("Caño de PVC reforzado de 110 mm por 4 metros con junta elástica y aro de goma", 650m, 700m)]);

        var tilde = Tildes(d)[0];
        var derecha = tilde.TranslatePoint(new Point(tilde.Bounds.Width, 0), Parte<Grid>(d, "Filas"))!.Value.X;
        var texto = tilde.GetVisualDescendants().OfType<TextBlock>().First();
        Assert.True(texto.Bounds.Height > 20, "El nombre largo no envolvió.");
        Assert.True(derecha <= Parte<Grid>(d, "Filas").Bounds.Width + 0.5);
    }

    private static object? Foco(Window d) => d.FocusManager!.GetFocusedElement();

    [AvaloniaFact]
    public void Tab_NoSacaElFocoDelDialogo()
    {
        var (d, _) = Abrir();
        var contenido = Parte<Border>(d, "Contenido");

        for (var i = 0; i < 8; i++)
        {
            d.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            var foco = Assert.IsAssignableFrom<Visual>(Foco(d));
            Assert.True(foco == contenido || contenido.IsVisualAncestorOf(foco), $"Tab {i + 1}: el foco salió del diálogo.");
        }
    }

    [AvaloniaFact]
    public void Botones_SinRecorteDentroDelAnchoUtil()
    {
        var (d, _) = Abrir();
        var contenido = Parte<Border>(d, "Contenido");
        foreach (var b in new[] { Parte<Button>(d, "CancelarButton"), Parte<Button>(d, "ConfirmarButton") })
        {
            var texto = b.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == (string)b.Content!);
            texto.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var util = b.Bounds.Width - b.Padding.Left - b.Padding.Right - b.BorderThickness.Left - b.BorderThickness.Right;
            Assert.True(texto.DesiredSize.Width <= util + 0.5, $"{b.Content}: pide {texto.DesiredSize.Width} y tiene {util}.");
            var derecha = b.TranslatePoint(new Point(b.Bounds.Width, 0), contenido)!.Value.X;
            Assert.True(derecha <= contenido.Bounds.Width - contenido.Padding.Right + 0.5, $"{b.Content} termina en {derecha:F1}.");
        }
    }

    [AvaloniaFact]
    public void LosPrecios_LlevanLaTipografiaDelTilde_ConCifrasTabulares()
    {
        var (d, _) = Abrir([Cambio("Pala", 1500.5m, 2000m)]);
        var tilde = Tildes(d)[0];
        var nombre = tilde.GetVisualDescendants().OfType<TextBlock>().First();
        var precios = Parte<Grid>(d, "Filas").Children.OfType<TextBlock>().Where(t => t.Text!.StartsWith("$")).ToList();

        Assert.Equal(2, precios.Count);
        foreach (var p in precios)
        {
            Assert.Equal(tilde.FontSize, p.FontSize);
            Assert.Equal(nombre.Foreground, p.Foreground);
            Assert.Contains(p.FontFeatures!, f => f.ToString().Contains("tnum"));
        }
    }

    [AvaloniaFact]
    public async Task Servicio_SinVentanaPrincipal_DevuelveNullSinAbrir() =>
        Assert.Null(await new ConfirmacionPreciosDialogService().ConfirmarCambiosAsync(Tres()));
}
