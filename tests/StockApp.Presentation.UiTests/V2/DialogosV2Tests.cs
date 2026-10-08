using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.Views.Dialogs;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Diálogos v2 (spec §5.5): siguen sin bindings, con acceso por x:Name. Review Focus 5: un
/// x:Name o un Click perdido cambia el resultado que recibe ConfirmacionService.
/// </summary>
public class DialogosV2Tests
{
    private static Task<T> Abrir<T>(Window dialogo)
    {
        var duenio = new Window();
        duenio.Show();
        var resultado = dialogo.ShowDialog<T>(duenio);
        Dispatcher.UIThread.RunJobs();
        return resultado;
    }

    private static void Clic(Window dialogo, string boton)
    {
        dialogo.FindControl<Button>(boton)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("ConfirmarButton", true)]
    [InlineData("CancelarButton", false)]
    public void Confirmacion_DevuelveSegunElBoton(string boton, bool esperado)
    {
        var dialogo = new ConfirmacionDialog("¿Cerrar la sesión?");
        var resultado = Abrir<bool>(dialogo);

        Clic(dialogo, boton);

        Assert.True(resultado.IsCompleted);
        Assert.Equal(esperado, resultado.Result);
    }

    [AvaloniaFact]
    public void Confirmacion_SinTextosPropios_MantieneCancelarYSiContinuar()
    {
        var dialogo = new ConfirmacionDialog("¿Cerrar la sesión?");

        Assert.Equal("Cancelar", dialogo.FindControl<Button>("CancelarButton")!.Content);
        Assert.Equal("Sí, continuar", dialogo.FindControl<Button>("ConfirmarButton")!.Content);
    }

    [AvaloniaTheory]
    [InlineData("ConfirmarButton", true)]
    [InlineData("CancelarButton", false)]
    public void Confirmacion_ConTextosPropios_MuestraLosTextosYDevuelveSegunElBoton(string boton, bool esperado)
    {
        var dialogo = new ConfirmacionDialog(
            "Entrada registrada. ¿Desea asociar una factura de proveedor a esta entrada?", "No, continuar", "Sí, adjuntar factura");
        var resultado = Abrir<bool>(dialogo);
        var negativo = dialogo.FindControl<Button>("CancelarButton")!;
        var afirmativo = dialogo.FindControl<Button>("ConfirmarButton")!;

        Assert.Equal("No, continuar", negativo.Content);
        Assert.Equal("Sí, adjuntar factura", afirmativo.Content);
        // Sin recorte: cada botón mide al menos lo que pide su texto y la fila entra en el ancho útil del diálogo.
        foreach (var b in new[] { negativo, afirmativo })
        {
            var texto = b.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == (string)b.Content!);
            Assert.True(texto.Bounds.Width + b.Padding.Left + b.Padding.Right <= b.Bounds.Width + 0.5,
                $"{b.Content}: el texto mide {texto.Bounds.Width} px en un botón de {b.Bounds.Width} px.");
        }
        var contenido = (Border)dialogo.Content!;
        foreach (var b in new[] { negativo, afirmativo })
        {
            var izquierda = b.TranslatePoint(new Point(0, 0), contenido)!.Value.X;
            Assert.True(izquierda >= contenido.Padding.Left - 0.5 && izquierda + b.Bounds.Width <= contenido.Bounds.Width - contenido.Padding.Right + 0.5,
                $"{b.Content}: el botón ocupa de {izquierda} a {izquierda + b.Bounds.Width} px y el diálogo útil llega a {contenido.Bounds.Width - contenido.Padding.Right} px.");
        }

        Clic(dialogo, boton);

        Assert.True(resultado.IsCompleted);
        Assert.Equal(esperado, resultado.Result);
    }

    [AvaloniaFact]
    public void PedirTexto_AceptarDevuelveElTexto_CancelarDevuelveNull()
    {
        var aceptar = new PedirTextoDialog("Anular documento", "Ingresá el motivo de la anulación:");
        var conTexto = Abrir<string?>(aceptar);
        aceptar.FindControl<TextBox>("TextoTextBox")!.Text = "Error de carga";
        Clic(aceptar, "AceptarButton");
        Assert.True(conTexto.IsCompleted, "Aceptar no cerró el diálogo (Click perdido).");
        Assert.Equal("Error de carga", conTexto.Result);

        var cancelar = new PedirTextoDialog("Anular documento", "Ingresá el motivo de la anulación:");
        var sinTexto = Abrir<string?>(cancelar);
        Clic(cancelar, "CancelarButton");
        Assert.True(sinTexto.IsCompleted, "Cancelar no cerró el diálogo (Click perdido).");
        Assert.Null(sinTexto.Result);
    }

    [AvaloniaFact]
    public void Mensaje_AceptarCierra()
    {
        var dialogo = new MensajeDialog("Aviso");
        var duenio = new Window();
        duenio.Show();
        var cierre = dialogo.ShowDialog(duenio);
        Dispatcher.UIThread.RunJobs();

        Clic(dialogo, "AceptarButton");

        Assert.True(cierre.IsCompleted);
    }

    [AvaloniaFact]
    public void LosTres_TienenUi2EnElBorderRaizYUnSoloPrimario()
    {
        foreach (Window dialogo in new Window[] { new ConfirmacionDialog("x"), new MensajeDialog("x"), new PedirTextoDialog("t", "x") })
        {
            var contenido = Assert.IsType<Border>(dialogo.Content);
            Assert.Equal("Contenido", contenido.Name);
            Assert.Contains("ui2", contenido.Classes);
            var primarios = new[] { "ConfirmarButton", "AceptarButton", "CancelarButton" }
                .Select(n => dialogo.FindControl<Button>(n))
                .Count(b => b is not null && b.Classes.Contains("primario"));
            Assert.Equal(1, primarios);
        }
    }
}
