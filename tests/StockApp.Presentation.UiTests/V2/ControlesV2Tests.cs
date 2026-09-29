using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Optris.Icons.Avalonia;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

public class ControlesV2Tests
{
    [AvaloniaTheory]
    [InlineData("primario", "#1D4ED8", "#1D4ED8", "#FFFFFF")]
    [InlineData("secundario", "#FFFFFF", "#D6D3D1", "#44403C")]
    [InlineData("peligro", "#FFFFFF", "#D6D3D1", "#B91C1C")]
    public void Boton_Variante_ColoresAlto36Radio8Fuente13SemiBold(string clase, string fondo, string borde, string texto)
    {
        var boton = Buscar<Button>(Montar($"""<Button x:Name="B" Classes="{clase}" Content="Guardar" />"""), "B");

        Assert.Equal(Hex(fondo), ColorDe(boton.Background));
        Assert.Equal(Hex(borde), ColorDe(boton.BorderBrush));
        Assert.Equal(Hex(texto), ColorDe(boton.Foreground));
        Assert.Equal(36, boton.Bounds.Height);
        Assert.Equal(new CornerRadius(8), boton.CornerRadius);
        Assert.Equal(13, boton.FontSize);
        Assert.Equal(FontWeight.SemiBold, boton.FontWeight);
    }

    [AvaloniaFact]
    public void BotonGhost_NoArrastraNadaDelGhostViejo()
    {
        // Review Focus 1: Button.ghost viejo (Controls.axaml) también matchea dentro de .ui2.
        var boton = Buscar<Button>(Montar("""<Button x:Name="B" Classes="ghost" Content="Limpiar filtros" />"""), "B");

        Assert.Equal(Colors.Transparent, ColorDe(boton.Background));
        Assert.Equal(Hex("#44403C"), ColorDe(boton.Foreground));
        Assert.Equal(HorizontalAlignment.Center, boton.HorizontalContentAlignment);
        Assert.Equal(new Thickness(16, 0), boton.Padding);
        Assert.Equal(new Thickness(1), boton.BorderThickness);
        Assert.Equal(36, boton.Bounds.Height);
    }

    [AvaloniaFact]
    public void BotonPrimario_Hover_PintaAcentoHoverEnElPresenter()
    {
        var boton = Buscar<Button>(Montar("""<Button x:Name="B" Classes="primario" Content="Guardar" />"""), "B");

        ((IPseudoClasses)boton.Classes).Set(":pointerover", true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#1E40AF"), ColorDe(Parte<ContentPresenter>(boton, "PART_ContentPresenter").Background));
    }

    [AvaloniaFact]
    public void BotonSecundario_Hover_NoUsaElVerdeViejo()
    {
        var boton = Buscar<Button>(Montar("""<Button x:Name="B" Classes="secundario" Content="Cancelar" />"""), "B");

        ((IPseudoClasses)boton.Classes).Set(":pointerover", true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#F5F5F4"), ColorDe(Parte<ContentPresenter>(boton, "PART_ContentPresenter").Background));
    }

    [AvaloniaTheory]
    [InlineData("""<TextBox x:Name="C" Text="Tornillo" />""")]
    [InlineData("""<ComboBox x:Name="C" />""")]
    [InlineData("""<NumericUpDown x:Name="C" Value="12" />""")]
    [InlineData("""<CalendarDatePicker x:Name="C" />""")]
    [InlineData("""<AutoCompleteBox x:Name="C" />""")]
    public void Inputs_MidenAlto36(string xaml)
    {
        // Review Focus 5: el TextBox interno de NumericUpDown/CalendarDatePicker recibe los estilos
        // de TextBox; sin el reseteo de su MinHeight el control mide 38.
        Assert.Equal(36, Buscar<Control>(Montar(xaml), "C").Bounds.Height);
    }

    [AvaloniaTheory]
    [InlineData("""<TextBox x:Name="C" Width="200" Text="Tornillo" />""")]
    [InlineData("""<ComboBox x:Name="C" Width="200" SelectedIndex="0"><ComboBoxItem>Tornillo</ComboBoxItem></ComboBox>""")]
    [InlineData("""<NumericUpDown x:Name="C" Width="200" Value="340" FormatString="0" />""")]
    [InlineData("""<CalendarDatePicker x:Name="C" Width="200" SelectedDate="2026-09-28" SelectedDateFormat="Custom" CustomDateFormatString="dd/MM/yyyy" />""")]
    public void Inputs_TextoEmpiezaA13DelBordeExterior(string xaml)
    {
        // 1 de borde + 12 de padding, igual que .v2-input del mockup. ComboBox y NumericUpDown
        // necesitan Padding 13 para llegar (su plantilla no descuenta el borde).
        var control = Buscar<Control>(Montar(xaml), "C");
        var texto = control.GetVisualDescendants().OfType<Control>()
            .First(c => c is TextPresenter || (c is TextBlock t && t.Text == "Tornillo"));

        Assert.Equal(13, texto.TranslatePoint(new Point(0, 0), control)!.Value.X);
    }

    [AvaloniaFact]
    public void ComboBoxEditable_SinDobleBordeYAlto36ConTextoA13()
    {
        // F1 (review Task 10): PART_EditableTextBox es un TextBox real (verificado decompilando
        // Avalonia.Themes.Fluent 12.0.5), igual que PART_TextBox de NumericUpDown/CalendarDatePicker;
        // el selector genérico ".ui2 TextBox" lo alcanza y sin resetearlo un ComboBox editable mide
        // 38 con doble borde (usado 5 veces en NuevaImportacionView.axaml).
        var combo = Buscar<ComboBox>(Montar("""<ComboBox x:Name="C" Width="200" IsEditable="True" Text="Tornillo" />"""), "C");
        var editable = Parte<TextBox>(combo, "PART_EditableTextBox");

        Assert.Equal(36, combo.Bounds.Height);
        Assert.Equal(new Thickness(0), editable.BorderThickness);

        var texto = editable.GetVisualDescendants().OfType<TextPresenter>().First();
        Assert.Equal(13, texto.TranslatePoint(new Point(0, 0), combo)!.Value.X);
    }

    [AvaloniaFact]
    public void CalendarDatePicker_GlifoEsIconoMdiSinElDiaDeHoy()
    {
        var picker = Buscar<CalendarDatePicker>(Montar("""<CalendarDatePicker x:Name="C" />"""), "C");
        var boton = Parte<Button>(picker, "PART_Button");

        Assert.DoesNotContain(boton.GetVisualDescendants().OfType<TextBlock>(), t => !string.IsNullOrEmpty(t.Text));
        Assert.Equal("mdi-calendar-blank-outline", Parte<Icon>(boton, "PART_GlifoFecha").Value);
    }

    [AvaloniaFact]
    public void TextBox_BordeInputRadio8Fuente13TextoEtiqueta()
    {
        var entrada = Buscar<TextBox>(Montar("""<TextBox x:Name="T" Text="Tornillo" />"""), "T");

        Assert.Equal(Hex("#D6D3D1"), ColorDe(entrada.BorderBrush));
        Assert.Equal(Hex("#FFFFFF"), ColorDe(entrada.Background));
        Assert.Equal(Hex("#44403C"), ColorDe(entrada.Foreground));
        Assert.Equal(new CornerRadius(8), entrada.CornerRadius);
        Assert.Equal(13, entrada.FontSize);
        Assert.Equal(new Thickness(12, 0), entrada.Padding);
    }

    [AvaloniaFact]
    public void TextBox_ConFoco_PintaElBordeDeAcento()
    {
        var ventana = Montar("""<TextBox x:Name="T" />""");
        var entrada = Buscar<TextBox>(ventana, "T");
        ventana.Activate();

        entrada.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#1D4ED8"), ColorDe(Parte<Border>(entrada, "PART_BorderElement").BorderBrush));
    }

    [AvaloniaFact]
    public void TextBox_ConErrores_PintaElBordeDeError()
    {
        var entrada = Buscar<TextBox>(Montar("""<TextBox x:Name="T" />"""), "T");

        DataValidationErrors.SetErrors(entrada, new object[] { "Campo requerido" });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#DC2626"), ColorDe(Parte<Border>(entrada, "PART_BorderElement").BorderBrush));
    }

    [AvaloniaFact]
    public void TextBox_DentroDeUi2_ConservaElErrorConverterGlobal()
    {
        // Review Focus 2: el seam de Themes/Controls.axaml tiene que seguir llegando adentro de .ui2.
        var entrada = Buscar<TextBox>(Montar("""<TextBox x:Name="T" />"""), "T");

        Assert.NotNull(DataValidationErrors.GetErrorConverter(entrada));
    }

    [AvaloniaFact]
    public void CheckBox_Casilla16Radio4BordeInput()
    {
        var casilla = Parte<Border>(Buscar<CheckBox>(Montar("""<CheckBox x:Name="C" Content="Activo" />"""), "C"), "NormalRectangle");

        Assert.Equal(16, casilla.Bounds.Width);
        Assert.Equal(16, casilla.Bounds.Height);
        Assert.Equal(new CornerRadius(4), casilla.CornerRadius);
        Assert.Equal(Hex("#D6D3D1"), ColorDe(casilla.BorderBrush));
    }

    [AvaloniaFact]
    public void CheckBox_Marcado_CasillaEnAcento()
    {
        var casilla = Parte<Border>(Buscar<CheckBox>(Montar("""<CheckBox x:Name="C" Content="Activo" IsChecked="True" />"""), "C"), "NormalRectangle");

        Assert.Equal(Hex("#1D4ED8"), ColorDe(casilla.Background));
        Assert.Equal(Hex("#1D4ED8"), ColorDe(casilla.BorderBrush));
    }

    [AvaloniaTheory]
    [InlineData("titulo-pagina", 22, 600, "#1C1917")]
    [InlineData("titulo-seccion", 15, 600, "#1C1917")]
    [InlineData("cuerpo", 14, 400, "#1C1917")]
    [InlineData("texto-secundario", 13, 400, "#57534E")]
    [InlineData("subtitulo-formulario", 13, 600, "#1C1917")]
    [InlineData("etiqueta-campo", 12, 500, "#44403C")]
    [InlineData("pie-conteo", 12, 400, "#78716C")]
    public void Tipografia_ClaseV2_TamanoPesoYColorDeLaSpec(string clase, double tamano, int peso, string color)
    {
        var texto = Buscar<TextBlock>(Montar($"""<TextBlock x:Name="T" Classes="{clase}" Text="Productos" />"""), "T");

        Assert.Equal(tamano, texto.FontSize);
        Assert.Equal((FontWeight)peso, texto.FontWeight);
        Assert.Equal(Hex(color), ColorDe(texto.Foreground));
    }

    [AvaloniaFact]
    public void TituloPagina_AltoDeLinea26()
    {
        var texto = Buscar<TextBlock>(Montar("""<TextBlock x:Name="T" Classes="titulo-pagina" Text="Productos" />"""), "T");

        Assert.Equal(26, texto.LineHeight);
    }
}
