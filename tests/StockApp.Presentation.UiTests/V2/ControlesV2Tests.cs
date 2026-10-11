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
    public void TextBox_ConErrores_MensajeA4DelBordeInferior()
    {
        // Referencia aprobada (patrones-1 .pt-error-msg) y base.css .v2-error-campo: margin-top 4.
        // El DataValidationErrors de Fluent ya pone Margin 0,4,0,0 en InlineDataValidationContentControl
        // (medido en el catálogo, Task 17): un margen propio en el ErrorTemplate lo duplicaba a 8.
        var ventana = Montar("""<TextBox x:Name="T" />""");
        var entrada = Buscar<TextBox>(ventana, "T");

        DataValidationErrors.SetErrors(entrada, new object[] { "Campo requerido" });
        Dispatcher.UIThread.RunJobs();
        ventana.UpdateLayout();

        var borde = Parte<Border>(entrada, "PART_BorderElement");
        var mensaje = Assert.Single(ventana.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Campo requerido");
        var inferiorBorde = borde.TranslatePoint(new Point(0, borde.Bounds.Height), ventana)!.Value.Y;
        var superiorMensaje = mensaje.TranslatePoint(new Point(0, 0), ventana)!.Value.Y;
        Assert.Equal(4, superiorMensaje - inferiorBorde);
    }

    [AvaloniaFact]
    public void TextBox_Placeholder_OpacoEnTerciario()
    {
        // Fluent dibuja PART_Placeholder con Opacity 0.5: el #78716C del token salía #BBB8B5
        // (medido en el catálogo, Task 17). base.css .v2-input-placeholder es el terciario pleno.
        var entrada = Buscar<TextBox>(Montar("""<TextBox x:Name="T" PlaceholderText="Buscar" />"""), "T");

        var placeholder = Parte<TextBlock>(entrada, "PART_Placeholder");
        Assert.Equal(Hex("#78716C"), ColorDe(placeholder.Foreground));
        Assert.Equal(1, placeholder.Opacity);
    }

    [AvaloniaFact]
    public void TextBox_DentroDeUi2_ConservaElErrorConverterGlobal()
    {
        // Review Focus 2: el seam de Themes/Controls.axaml tiene que seguir llegando adentro de .ui2.
        var entrada = Buscar<TextBox>(Montar("""<TextBox x:Name="T" />"""), "T");

        Assert.NotNull(DataValidationErrors.GetErrorConverter(entrada));
    }

    [AvaloniaFact]
    public void Advertencia_GanaSobreTituloDeSeccion()
    {
        var ventana = Montar("""
            <StackPanel>
              <TextBlock x:Name="Neutro" Classes="titulo-seccion" Text="$ 0,00" />
              <TextBlock x:Name="Aviso" Classes="titulo-seccion advertencia" Text="$ 60,00" />
            </StackPanel>
            """);

        Assert.Equal(Hex("#1C1917"), ColorDe(Buscar<TextBlock>(ventana, "Neutro").Foreground));
        Assert.Equal(Hex("#92400E"), ColorDe(Buscar<TextBlock>(ventana, "Aviso").Foreground));
    }

    [AvaloniaFact]
    public void ComboBoxYCheckBox_Deshabilitados_UsanLosTokensDeDeshabilitadoV2()
    {
        // Sin estos estilos caen en el gris de Fluent (#CCCCCC el combo, #9F9F9F el texto del tilde).
        var ventana = Montar("""
            <StackPanel>
              <ComboBox x:Name="Combo" Width="200" IsEnabled="False" SelectedIndex="0"><ComboBoxItem>Tornillo</ComboBoxItem></ComboBox>
              <CheckBox x:Name="Tilde" IsEnabled="False" Content="Compra a crédito" />
              <CheckBox x:Name="TildeMarcado" IsEnabled="False" IsChecked="True" Content="Marcado" />
            </StackPanel>
            """);
        var combo = Buscar<ComboBox>(ventana, "Combo");
        var tilde = Buscar<CheckBox>(ventana, "Tilde");
        var casilla = Parte<Border>(tilde, "NormalRectangle");

        Assert.Equal(Hex("#F5F5F4"), ColorDe(Parte<Border>(combo, "Background").Background));
        Assert.Equal(Hex("#E7E5E4"), ColorDe(Parte<Border>(combo, "Background").BorderBrush));
        Assert.Equal(Hex("#A8A29E"), ColorDe(combo.Foreground));
        // Lo que se ve es el texto de la selección dentro de la plantilla, no la propiedad del control.
        var texto = combo.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Tornillo");
        Assert.Equal(Hex("#A8A29E"), ColorDe(texto.Foreground));
        Assert.Equal(Hex("#A8A29E"), ColorDe(texto.GetVisualAncestors().OfType<ContentPresenter>().First().Foreground));
        Assert.Equal(Hex("#A8A29E"), ColorDe(Parte<PathIcon>(combo, "DropDownGlyph").Foreground));
        Assert.Equal(Hex("#A8A29E"), ColorDe(tilde.Foreground));
        Assert.Equal(Hex("#F5F5F4"), ColorDe(casilla.Background));
        Assert.Equal(Hex("#E7E5E4"), ColorDe(casilla.BorderBrush));
        // Marcado y deshabilitado: la casilla conserva el deshabilitado, no el azul de acento.
        var marcada = Parte<Border>(Buscar<CheckBox>(ventana, "TildeMarcado"), "NormalRectangle");
        Assert.Equal(Hex("#F5F5F4"), ColorDe(marcada.Background));
        Assert.Equal(Hex("#E7E5E4"), ColorDe(marcada.BorderBrush));
        Assert.Equal(Hex("#A8A29E"), ColorDe(Parte<Avalonia.Controls.Shapes.Path>(Buscar<CheckBox>(ventana, "TildeMarcado"), "CheckGlyph").Fill));
    }

    [AvaloniaFact]
    public void CalendarDatePicker_Deshabilitado_UsaLosTokensDeDeshabilitadoV2()
    {
        // Fluent lo pinta con ^:disabled sobre Border#Background y Button#PART_Button; .ui2 no tiene :disabled.
        var ventana = Montar("""
            <CalendarDatePicker x:Name="Fecha" Width="200" IsEnabled="False" SelectedDate="2026-10-11" />
            """);
        var fecha = Buscar<CalendarDatePicker>(ventana, "Fecha");

        Assert.Equal(Hex("#F5F5F4"), ColorDe(Parte<Border>(fecha, "Background").Background));
        Assert.Equal(Hex("#E7E5E4"), ColorDe(Parte<Border>(fecha, "Background").BorderBrush));
        // El texto visible lo dibuja el TextPresenter del TextBox interno, no la propiedad del control.
        Assert.False(string.IsNullOrEmpty(Parte<TextBox>(fecha, "PART_TextBox").Text));
        Assert.Equal(Hex("#A8A29E"), ColorDe(fecha.GetVisualDescendants().OfType<TextPresenter>().First().Foreground));
        Assert.Equal(Hex("#A8A29E"), ColorDe(Parte<Icon>(fecha, "PART_GlifoFecha").Foreground));
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
    public void CheckBox_CasillaCentradaEnElAltoDeControl()
    {
        // La plantilla Fluent arma la caja en un Grid de 32 con VerticalAlignment Top: con el alto
        // v2 de 36 la casilla quedaba 2px por encima del centro del texto (medido en el catálogo,
        // Task 17). base.css .v2-casilla-fila la centra (align-items: center).
        var ventana = Montar("""<CheckBox x:Name="C" Content="Activo" VerticalAlignment="Top" />""");
        var check = Buscar<CheckBox>(ventana, "C");
        var casilla = Parte<Border>(check, "NormalRectangle");

        Assert.Equal(36, check.Bounds.Height);
        Assert.Equal(10, casilla.TranslatePoint(new Point(0, 0), check)!.Value.Y);
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
