using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Presentation.ViewModels.Tareas;
using StockApp.Presentation.Views.Tareas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Diálogo "Reclasificar tarea" v2 con el Window REAL: scope ui2 en el contenido, Aceptar como único primario,
/// Escape que cancela (devuelve null) y Enter que confirma con los datos del panel, y el panel sin recortes con un valor
/// largo elegido.</summary>
public class ReclasificarTareaDialogV2Tests
{
    private static readonly Zona Centro = new() { Id = 1, Nombre = "Centro", Activo = true };
    private static readonly Zona Costa = new() { Id = 2, Nombre = "Costa", Activo = true };

    private static async Task<(ReclasificarTareaDialog Dialogo, ReclasificarTareaDialogViewModel Vm, Task<DatosClasificacionTarea?> Resultado)> Abrir(
        DatosClasificacionTarea? actual = null)
    {
        var panel = new ClasificacionTareaPanelViewModel(new ZonaServiceFake([Centro, Costa]), new DimensionTematicaServiceFake(),
            new OrganismoResponsableServiceFake(), new OrigenFinanciamientoServiceFake(), new DocumentosFake(), new ConfirmacionServiceFake());
        await panel.InicializarAsync(actual ?? new DatosClasificacionTarea(1, null, null, null, null));
        var vm = new ReclasificarTareaDialogViewModel(panel);
        var dueno = new Window();
        dueno.Show();
        var dialogo = new ReclasificarTareaDialog { DataContext = vm };
        var resultado = dialogo.ShowDialog<DatosClasificacionTarea?>(dueno);
        Dispatcher.UIThread.RunJobs();
        return (dialogo, vm, resultado);
    }

    private static T Parte<T>(Control c, string nombre) where T : Control => MontajeV2.Parte<T>(c, nombre);

    [AvaloniaFact]
    public async Task Contenido_TieneUi2YAceptarEsElUnicoPrimario()
    {
        var (dialogo, _, _) = await Abrir();

        var contenido = Parte<Border>(dialogo, "Contenido");
        Assert.Contains("ui2", contenido.Classes);
        Assert.Equal(500, dialogo.Width);
        var primarios = dialogo.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primario")).ToList();
        Assert.Same(Parte<Button>(dialogo, "AceptarButton"), Assert.Single(primarios));
        Assert.True(Parte<Button>(dialogo, "CancelarButton").IsCancel);
        Assert.True(Parte<Button>(dialogo, "AceptarButton").IsDefault);
        dialogo.Close();
    }

    [AvaloniaFact]
    public async Task Escape_CierraYDevuelveNull()
    {
        var (dialogo, vm, resultado) = await Abrir();
        vm.Panel.ZonaSeleccionada = vm.Panel.ZonasDisponibles.Last();

        dialogo.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(resultado.IsCompleted, "Escape no cerró el diálogo.");
        Assert.Null(await resultado);
    }

    [AvaloniaFact]
    public async Task Enter_EnElDialogo_ConfirmaConLosDatosDelPanel()
    {
        var (dialogo, vm, resultado) = await Abrir(new DatosClasificacionTarea(1, null, null, null, null));
        vm.Panel.ZonaSeleccionada = vm.Panel.ZonasDisponibles.Single(z => z.Id == 2);
        Parte<AutoCompleteBox>(dialogo, "BuscadorExpediente").Focus();
        Dispatcher.UIThread.RunJobs();

        dialogo.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(resultado.IsCompleted, "Enter no confirmó el diálogo.");
        var datos = await resultado;
        Assert.NotNull(datos);
        Assert.Equal(2, datos!.ZonaId);
    }

    [AvaloniaFact]
    public async Task PanelEnElDialogo_EtiquetasYValoresSinRecorte()
    {
        var (dialogo, vm, _) = await Abrir(new DatosClasificacionTarea(12, null, null, null, null));
        Assert.Equal("(dado de baja — id 12)", vm.Panel.ZonaSeleccionada!.Nombre);

        foreach (var nombre in new[] { "CampoZona", "CampoDimension", "CampoOrganismo", "CampoOrigen" })
        {
            var campo = Parte<Control>(dialogo, nombre);
            var etiqueta = campo.GetVisualDescendants().OfType<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text));
            var natural = new TextBlock { Text = etiqueta.Text, FontSize = etiqueta.FontSize, FontWeight = etiqueta.FontWeight, FontFamily = etiqueta.FontFamily };
            natural.Measure(Size.Infinity);
            Assert.True(etiqueta.Bounds.Width >= natural.DesiredSize.Width - 0.5 && campo.Bounds.Width >= natural.DesiredSize.Width - 0.5,
                $"'{etiqueta.Text}' no entra: {campo.Bounds.Width:0.#} de {natural.DesiredSize.Width:0.#}.");
        }
        var combo = Parte<ComboBox>(dialogo, "ComboZona");
        var texto = combo.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "(dado de baja — id 12)");
        var medido = new TextBlock { Text = texto.Text, FontSize = texto.FontSize, FontFamily = texto.FontFamily, FontWeight = texto.FontWeight };
        medido.Measure(Size.Infinity);
        Assert.True(texto.Bounds.Width >= medido.DesiredSize.Width - 0.5, $"El valor elegido se recorta: {texto.Bounds.Width:0.#} de {medido.DesiredSize.Width:0.#}.");
        dialogo.Close();
    }
}
