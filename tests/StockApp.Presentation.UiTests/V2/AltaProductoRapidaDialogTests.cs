using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.Views.Dialogs;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Alta rápida de producto (convención 25): el diálogo real, sin VM. Un IsCancel o un IsDefault perdido cambia lo que
/// hace el teclado; una validación perdida da de alta un producto sin unidad.
/// </summary>
public class AltaProductoRapidaDialogTests
{
    private static readonly IReadOnlyList<Categoria> Categorias =
        [new() { Id = 1, Nombre = "Construcción" }, new() { Id = 3, Nombre = "Herramientas" }];
    private static readonly IReadOnlyList<UnidadMedida> Unidades =
        [new() { Id = 1, Nombre = "Unidad", Abreviatura = "u" }, new() { Id = 2, Nombre = "Bolsa", Abreviatura = "bol" }];

    private static (AltaProductoRapidaDialog Dialogo, Task<ProductoNuevoDto?> Resultado) Abrir()
    {
        var dialogo = new AltaProductoRapidaDialog(Categorias, Unidades);
        var duenio = new Window();
        duenio.Show();
        var resultado = dialogo.ShowDialog<ProductoNuevoDto?>(duenio);
        Dispatcher.UIThread.RunJobs();
        return (dialogo, resultado);
    }

    private static T Parte<T>(Window d, string nombre) where T : Control => d.FindControl<T>(nombre)!;

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

    private static void Completar(Window d, string codigo, string nombre, int? categoriaId, int? unidadId)
    {
        Parte<TextBox>(d, "CodigoTextBox").Text = codigo;
        Parte<TextBox>(d, "NombreTextBox").Text = nombre;
        Parte<ComboBox>(d, "CategoriaCombo").SelectedItem = Categorias.SingleOrDefault(c => c.Id == categoriaId);
        Parte<ComboBox>(d, "UnidadCombo").SelectedItem = Unidades.SingleOrDefault(u => u.Id == unidadId);
        Dispatcher.UIThread.RunJobs();
    }

    private static IInputElement? Foco(Window d) => d.FocusManager!.GetFocusedElement();

    [AvaloniaFact]
    public void Completo_ConfirmarDevuelveElProductoNuevo()
    {
        var (d, resultado) = Abrir();
        Completar(d, "MAT-0601", "Carretilla de obra 90 L", 3, 1);

        Clic(d, "ConfirmarButton");

        Assert.True(resultado.IsCompleted);
        Assert.Equal(new ProductoNuevoDto("MAT-0601", "Carretilla de obra 90 L", 3, 1), resultado.Result);
    }

    [AvaloniaFact]
    public void SinCategoria_DevuelveCategoriaNull()
    {
        var (d, resultado) = Abrir();
        Completar(d, "MAT-0601", "Carretilla de obra 90 L", null, 1);

        Clic(d, "ConfirmarButton");

        Assert.Null(resultado.Result!.CategoriaId);
    }

    [AvaloniaTheory]
    [InlineData("", "Carretilla", 1, "Falta completar: Código.")]
    [InlineData("MAT-0601", "   ", 1, "Falta completar: Nombre.")]
    [InlineData("MAT-0601", "Carretilla", null, "Falta completar: Unidad de medida.")]
    [InlineData("MAT-0601", "", null, "Falta completar: Nombre y Unidad de medida.")]
    [InlineData("", "", null, "Falta completar: Código, Nombre y Unidad de medida.")]
    public void ConFaltantes_NoConfirma_YLaAlertaLosNombra(string codigo, string nombre, int? unidadId, string esperado)
    {
        var (d, resultado) = Abrir();
        Completar(d, codigo, nombre, null, unidadId);

        Clic(d, "ConfirmarButton");

        Assert.False(resultado.IsCompleted);
        var alerta = Parte<Alerta>(d, "AlertaFaltantes");
        Assert.True(alerta.IsVisible);
        Assert.Equal(VarianteAlerta.Error, alerta.Variante);
        Assert.Equal(esperado, alerta.Texto);
    }

    [AvaloniaFact]
    public void SinFaltantes_LaAlertaNoSeVe() =>
        Assert.False(Parte<Alerta>(Abrir().Dialogo, "AlertaFaltantes").IsVisible);

    [AvaloniaFact]
    public void Cancelar_DevuelveNullAunqueEsteCompleto()
    {
        var (d, resultado) = Abrir();
        Completar(d, "MAT-0601", "Carretilla de obra 90 L", 3, 1);

        Clic(d, "CancelarButton");

        Assert.True(resultado.IsCompleted);
        Assert.Null(resultado.Result);
    }

    [AvaloniaFact]
    public void Escape_Cancela()
    {
        var (d, resultado) = Abrir();
        Completar(d, "MAT-0601", "Carretilla de obra 90 L", 3, 1);

        Tecla(d, Parte<TextBox>(d, "NombreTextBox"), PhysicalKey.Escape);

        Assert.True(resultado.IsCompleted);
        Assert.Null(resultado.Result);
    }

    [AvaloniaFact]
    public void Escape_ConElComboAbierto_PrimeroCierraElDesplegable()
    {
        var (d, resultado) = Abrir();
        var combo = Parte<ComboBox>(d, "UnidadCombo");
        combo.Focus();
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();

        combo.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Dispatcher.UIThread.RunJobs();

        Assert.False(combo.IsDropDownOpen);
        Assert.False(resultado.IsCompleted, "El primer Escape cerró el diálogo entero y perdió lo tipeado.");

        Tecla(d, combo, PhysicalKey.Escape);
        Assert.True(resultado.IsCompleted);
        Assert.Null(resultado.Result);
    }

    [AvaloniaFact]
    public void Enter_EnUnCampoConTodoCompleto_Confirma()
    {
        var (d, resultado) = Abrir();
        Completar(d, "MAT-0601", "Carretilla de obra 90 L", 3, 1);

        Tecla(d, Parte<TextBox>(d, "NombreTextBox"), PhysicalKey.Enter);

        Assert.True(resultado.IsCompleted);
        Assert.Equal("MAT-0601", resultado.Result!.Codigo);
    }

    [AvaloniaFact]
    public void Enter_ConFaltantes_NoCierraYMuestraLaAlerta()
    {
        var (d, resultado) = Abrir();
        Completar(d, "MAT-0601", "", null, null);

        Tecla(d, Parte<TextBox>(d, "CodigoTextBox"), PhysicalKey.Enter);

        Assert.False(resultado.IsCompleted);
        Assert.True(Parte<Alerta>(d, "AlertaFaltantes").IsVisible);
    }

    [AvaloniaFact]
    public void Abre_ConElFocoEnCodigo()
    {
        var (d, _) = Abrir();

        Assert.Same(Parte<TextBox>(d, "CodigoTextBox"), Foco(d));
    }

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
    public void Estructura_Ui2EnElBorderRaiz_UnSoloPrimario_YCancelarConfirmarConTeclado()
    {
        var d = new AltaProductoRapidaDialog(Categorias, Unidades);

        var contenido = Assert.IsType<Border>(d.Content);
        Assert.Equal("Contenido", contenido.Name);
        Assert.Contains("ui2", contenido.Classes);
        Assert.Equal("Producto nuevo", d.Title);
        Assert.Equal(440, d.Width);
        Assert.Equal(SizeToContent.Height, d.SizeToContent);
        Assert.False(d.CanResize);
        Assert.False(d.ShowInTaskbar);
        Assert.Equal(WindowStartupLocation.CenterOwner, d.WindowStartupLocation);
        var botones = contenido.GetLogicalDescendants().OfType<Button>().Where(b => b.Classes.Contains("primario")).ToList();
        Assert.Same(Parte<Button>(d, "ConfirmarButton"), Assert.Single(botones));
        Assert.True(Parte<Button>(d, "CancelarButton").IsCancel);
        Assert.True(Parte<Button>(d, "ConfirmarButton").IsDefault);
        Assert.Equal("Cancelar", Parte<Button>(d, "CancelarButton").Content);
        Assert.Equal("Confirmar", Parte<Button>(d, "ConfirmarButton").Content);
    }

    [AvaloniaFact]
    public void Requeridos_ConAsteriscoYCategoriaOpcional()
    {
        var d = new AltaProductoRapidaDialog(Categorias, Unidades);
        bool Requerido(Control control) => control.FindLogicalAncestorOfType<CampoFormulario>()!.Requerido;

        Assert.True(Requerido(Parte<TextBox>(d, "CodigoTextBox")));
        Assert.True(Requerido(Parte<TextBox>(d, "NombreTextBox")));
        Assert.True(Requerido(Parte<ComboBox>(d, "UnidadCombo")));
        Assert.False(Requerido(Parte<ComboBox>(d, "CategoriaCombo")));
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
    public async Task Servicio_SinVentanaPrincipal_DevuelveNullSinAbrir()
    {
        // TestApp no es un IClassicDesktopStyleApplicationLifetime: no hay MainWindow, valor seguro.
        var resultado = await new AltaProductoRapidaDialogService().PedirProductoNuevoAsync(Categorias, Unidades);

        Assert.Null(resultado);
    }
}
