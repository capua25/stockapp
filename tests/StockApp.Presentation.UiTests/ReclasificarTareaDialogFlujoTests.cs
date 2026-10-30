using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Proyectos;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Tareas;
using StockApp.Presentation.Views.Tareas;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Custodia por mutación (revisión post-Task 10, 2026-09-09; ampliada en la revisión de
/// integración 2026-09-09) del punto de mayor riesgo de ReclasificarTareaDialog: D21 exige
/// REEMPLAZO TOTAL — <c>OnAceptarClick</c> hace <c>Close(vm.Panel.ObtenerDatos())</c> SIN
/// filtrar los campos en null, que es justo lo que permite al Admin desasignar un clasificador
/// dejándolo vacío. Los dos tests de ClasificacionTareaDialogServiceTests/
/// ReclasificarTareaDialogViewModelTests (Presentation.Tests) solo cubren el guard headless y
/// el getter del panel — ninguno monta el diálogo real ni clickea "Aceptar", así que un
/// "arreglo" que filtre los nulls (una tentación razonable si no se conoce D21) no rompería
/// nada. Estos tests montan el diálogo REAL a través de
/// ClasificacionTareaDialogService.PedirClasificacionAsync (mismo mecanismo de
/// ApplicationLifetime por reflexión que ConfirmacionServiceDialogosConsecutivosTests) y
/// clickean el botón "Aceptar" real, no llaman a Close ni a OnAceptarClick a mano.
///
/// Important 2 (revisión de integración 2026-09-09): el primer test selecciona el ítem
/// centinela "(ninguno)" desde el ItemsSource del ComboBox REAL montado en el árbol visual,
/// probando que la opción existe de verdad y no solo en la firma del VM.
///
/// Spec 2026-10-30: el segundo test cubre que reclasificar NO pierda en silencio un proyecto
/// que se cerró después de vincularse a la tarea, con el Admin sin tocar ese campo.
/// </summary>
public class ReclasificarTareaDialogFlujoTests
{
    private static readonly TimeSpan TimeoutEsperaDialogo = TimeSpan.FromSeconds(5);

    [AvaloniaFact]
    public async Task ClickReal_EnAceptar_EligiendoLaOpcionNingunoDelComboReal_DevuelveElProyectoEnNullYElExpedienteIntacto()
    {
        var owner = new Window();
        owner.Show();

        var lifetime = new ClassicDesktopStyleApplicationLifetime { MainWindow = owner };
        InyectarApplicationLifetime(lifetime);

        var proyectos = new ProyectoOpcionesFake([new OpcionProyectoDto(1, "Plaza de deportes", EstadoProyecto.EnCurso)]);
        var documentos = new DocumentoServiceFake(new List<DocumentoAdministrativo>
        {
            new() { Id = 9, Numero = "0099", Anio = 2026, Tipo = TipoDocumento.Expediente, Descripcion = "x" },
        });

        var svc = new ClasificacionTareaDialogService(proyectos, documentos, new ConfirmacionServiceFake());

        var task = svc.PedirClasificacionAsync(new DatosClasificacionTarea(ProyectoId: 1, DocumentoAdministrativoId: 9));

        var dialog = (ReclasificarTareaDialog)await EsperarDialogoAsync(owner);
        Dispatcher.UIThread.RunJobs();

        var vm = (ReclasificarTareaDialogViewModel)dialog.DataContext!;

        // Confirma que InicializarAsync(actual) precargó de verdad la selección -- si esto
        // fallara, el resto del test estaría verificando un desvinculado que nunca estuvo vinculado.
        Assert.Equal(1, vm.Panel.ProyectoSeleccionado?.Id);
        Assert.Equal(9, vm.Panel.DocumentoSeleccionado?.Id);

        // El Admin deja vacío el proyecto (D21: equivale a desvincular) eligiendo "(ninguno)" en
        // el ComboBox REAL; el ítem sale del ItemsSource del control, no de la colección del VM.
        var comboProyecto = dialog.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ComboProyecto");
        var opcionNinguno = comboProyecto.ItemsSource!.Cast<OpcionProyectoTarea>().Single(o => o.Id is null);
        comboProyecto.SelectedItem = opcionNinguno;
        Dispatcher.UIThread.RunJobs();

        var botonAceptar = dialog.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, "Aceptar"));
        Clickear(dialog, botonAceptar);

        var resultado = await EsperarResultadoAsync(task);

        Assert.Equal(new DatosClasificacionTarea(null, 9), resultado);
    }

    [AvaloniaFact]
    public async Task ClickReal_EnAceptar_ConElProyectoActualCerrado_NoLoPierdeSiElAdminNoLoToca()
    {
        var owner = new Window();
        owner.Show();

        var lifetime = new ClassicDesktopStyleApplicationLifetime { MainWindow = owner };
        InyectarApplicationLifetime(lifetime);

        // El proyecto 7 de la tarea ya está Terminado: ListarOpcionesAsync solo trae el 8.
        var proyectos = new ProyectoOpcionesFake([new OpcionProyectoDto(8, "Rambla", EstadoProyecto.Pendiente)]);
        var svc = new ClasificacionTareaDialogService(proyectos, new DocumentoServiceFake(), new ConfirmacionServiceFake());

        var task = svc.PedirClasificacionAsync(new DatosClasificacionTarea(7, null), "Puente arroyo Las Vacas");

        var dialog = (ReclasificarTareaDialog)await EsperarDialogoAsync(owner);
        Dispatcher.UIThread.RunJobs();

        var vm = (ReclasificarTareaDialogViewModel)dialog.DataContext!;
        Assert.Equal(7, vm.Panel.ProyectoSeleccionado?.Id);
        Assert.Equal("Puente arroyo Las Vacas", vm.Panel.ProyectoSeleccionado?.Nombre);

        var botonAceptar = dialog.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, "Aceptar"));
        Clickear(dialog, botonAceptar);

        var resultado = await EsperarResultadoAsync(task);

        Assert.Equal(7, resultado?.ProyectoId);
    }

    private static void Clickear(Window window, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var centro = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        var puntoEnVentana = control.TranslatePoint(centro, window) ?? centro;
        window.MouseMove(puntoEnVentana);
        window.MouseDown(puntoEnVentana, MouseButton.Left);
        window.MouseUp(puntoEnVentana, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void InyectarApplicationLifetime(ClassicDesktopStyleApplicationLifetime lifetime)
    {
        var campo = typeof(Avalonia.Application).GetField(
            "_applicationLifetime", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(campo);
        campo!.SetValue(Avalonia.Application.Current, lifetime);
    }

    /// <summary>Mismo mecanismo que ConfirmacionServiceDialogosConsecutivosTests.EsperarDialogoAsync.</summary>
    private static async Task<Window> EsperarDialogoAsync(Window owner)
    {
        var aparecio = await EsperaMonotonica.HastaAsync(
            () => owner.OwnedWindows.Count > 0, TimeoutEsperaDialogo, TimeSpan.FromMilliseconds(10));

        if (!aparecio)
        {
            throw new TimeoutException(
                $"El diálogo nunca se creó/mostró como hijo de la ventana principal (timeout {TimeoutEsperaDialogo}).");
        }

        return owner.OwnedWindows[0];
    }

    private static async Task<DatosClasificacionTarea?> EsperarResultadoAsync(Task<DatosClasificacionTarea?> task)
    {
        var completada = await Task.WhenAny(task, Task.Delay(TimeoutEsperaDialogo));
        if (completada != task)
            throw new TimeoutException($"Se colgó esperando el resultado de PedirClasificacionAsync (timeout {TimeoutEsperaDialogo}).");

        return await task;
    }
}
