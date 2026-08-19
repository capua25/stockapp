using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// CampoFormulario agrega la etiqueta y nada mas. El punto critico esta en el ultimo test: el
/// control de adentro tiene que seguir recibiendo los Setter globales de Controls.axaml,
/// incluido (DataValidationErrors.ErrorConverter). Si el componente interceptara la validacion,
/// se romperia el blindaje de toda la app contra excepciones crudas llegando a la UI.
/// </summary>
public class CampoFormularioTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:c="using:StockApp.Presentation.Controls"
                Width="500" Height="200">
            <c:CampoFormulario x:Name="Campo" Etiqueta="Precio unitario">
                <TextBox x:Name="Entrada" />
            </c:CampoFormulario>
        </Window>
        """;

    private static Window Montar()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Montar_RenderizaLaEtiqueta()
    {
        var textos = Montar().GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Precio unitario", textos);
    }

    [AvaloniaFact]
    public void Montar_ElControlDelContenidoLlegaAlArbolYEsVisible()
    {
        var entrada = Montar().GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(t => t.Name == "Entrada");

        Assert.NotNull(entrada);
        Assert.True(ArbolVisual.EsVisibleEnArbol(entrada!));
    }

    [AvaloniaFact]
    public void Montar_ElTextBoxDeAdentroCONSERVAElErrorConverterGlobal()
    {
        // ESTE es el test que importa. Controls.axaml define el ErrorConverter por selector de
        // tipo "TextBox"; si CampoFormulario cambiara el tipo del control, lo envolviera en algo
        // que rompa el selector, o definiera su propio ErrorTemplate, este Setter dejaria de
        // aplicar y la app perderia el blindaje contra excepciones crudas de .NET en la UI.
        var entrada = Montar().GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "Entrada");

        var converter = DataValidationErrors.GetErrorConverter(entrada);

        Assert.NotNull(converter);
    }
}
