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
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
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
/// Important 2 (revisión de integración 2026-09-09): el primer test elegía "(ninguna)"
/// asignando null a mano a la propiedad del VM porque el ComboBox real no tenía esa opción en
/// su ItemsSource (Task 9). Ya corregido: ahora selecciona el ítem centinela desde el
/// ItemsSource del ComboBox REAL montado en el árbol visual, probando que la opción existe de
/// verdad y no solo en la firma del VM.
///
/// Important 1 (revisión de integración 2026-09-09): el segundo test cubre que reclasificar NO
/// pierda en silencio un clasificador dado de baja después de asignarse a la tarea, con el
/// Admin sin tocar ese campo.
///
/// Este proyecto no referencia Moq (mismo criterio que TareaServiceFake/DocumentoServiceFake):
/// los cuatro fakes de catálogo de acá abajo son privados a este archivo, a propósito, para no
/// pisar los fakes reusables de los 4 catálogos que le corresponde crear a la Task 11 del plan
/// (fakes de UiTests para los 4 catálogos + el diálogo de clasificación).
/// </summary>
public class ReclasificarTareaDialogFlujoTests
{
    private static readonly TimeSpan TimeoutEsperaDialogo = TimeSpan.FromSeconds(5);

    [AvaloniaFact]
    public async Task ClickReal_EnAceptar_EligiendoLaOpcionNingunaDelComboReal_DevuelveEseCampoEnNullYElRestoIntacto()
    {
        var owner = new Window();
        owner.Show();

        var lifetime = new ClassicDesktopStyleApplicationLifetime { MainWindow = owner };
        InyectarApplicationLifetime(lifetime);

        var zonas = new ZonaServiceStub(new Zona { Id = 1, Nombre = "Centro" });
        var dimensiones = new DimensionTematicaServiceStub(new DimensionTematica { Id = 3, Nombre = "Tránsito" });
        var organismos = new OrganismoResponsableServiceStub(new OrganismoResponsable { Id = 4, Nombre = "Intendencia" });
        var origenes = new OrigenFinanciamientoServiceStub(new OrigenFinanciamiento { Id = 5, Nombre = "Presupuesto propio" });
        var documentos = new DocumentoServiceFake();

        var svc = new ClasificacionTareaDialogService(
            zonas, dimensiones, organismos, origenes, documentos, new ConfirmacionServiceFake());

        // Clasificación actual completa: los cinco campos precargados (D21 — lo que el Admin ve
        // precargado, salvo que lo toque). DocumentoAdministrativoId queda null a propósito para
        // no depender de ObtenerPorIdAsync acá; el expediente no es el campo bajo prueba.
        var actual = new DatosClasificacionTarea(ZonaId: 1, DimensionTematicaId: 3,
            OrganismoResponsableId: 4, OrigenFinanciamientoId: 5, DocumentoAdministrativoId: null);

        var task = svc.PedirClasificacionAsync(actual);

        var dialog = (ReclasificarTareaDialog)await EsperarDialogoAsync(owner);
        Dispatcher.UIThread.RunJobs();

        var vm = (ReclasificarTareaDialogViewModel)dialog.DataContext!;

        // Confirma que InicializarAsync(actual) precargó de verdad la selección — si esto
        // fallara, el resto del test estaría verificando un desasignado que nunca estuvo asignado.
        Assert.Equal(1, vm.Panel.ZonaSeleccionada?.Id);
        Assert.Equal(3, vm.Panel.DimensionSeleccionada?.Id);
        Assert.Equal(4, vm.Panel.OrganismoSeleccionado?.Id);
        Assert.Equal(5, vm.Panel.OrigenSeleccionado?.Id);

        // El Admin "deja vacío" el campo Zona (D21: equivale a desasignar) eligiendo "(ninguna)"
        // en el ComboBox REAL montado en el árbol visual -- ubicado por su selección actual
        // (los cuatro combos comparten el mismo tipo OpcionClasificador, no alcanza con filtrar
        // por tipo como en DocumentoListViewTests). El ítem "(ninguna)" sale del ItemsSource
        // real del control, no de vm.Panel.ZonasDisponibles a secas, para probar que el combo
        // mismo lo expone.
        var comboZona = dialog.GetVisualDescendants().OfType<ComboBox>()
            .First(c => Equals(c.SelectedItem, vm.Panel.ZonaSeleccionada));
        var opcionNinguna = comboZona.ItemsSource!.Cast<OpcionClasificador>().Single(o => o.Id is null);
        comboZona.SelectedItem = opcionNinguna;
        Dispatcher.UIThread.RunJobs();

        var botonAceptar = dialog.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, "Aceptar"));
        Clickear(dialog, botonAceptar);

        var resultado = await EsperarResultadoAsync(task);

        Assert.NotNull(resultado);
        Assert.Null(resultado!.ZonaId);
        Assert.Equal(3, resultado.DimensionTematicaId);
        Assert.Equal(4, resultado.OrganismoResponsableId);
        Assert.Equal(5, resultado.OrigenFinanciamientoId);
        Assert.Null(resultado.DocumentoAdministrativoId);
    }

    [AvaloniaFact]
    public async Task ClickReal_EnAceptar_ConLaZonaActualDadaDeBaja_NoLaPierdeSiElAdminNoLaToca()
    {
        var owner = new Window();
        owner.Show();

        var lifetime = new ClassicDesktopStyleApplicationLifetime { MainWindow = owner };
        InyectarApplicationLifetime(lifetime);

        // La zona 7 de la tarea ya NO está entre las activas (dada de baja) -- solo queda la 8.
        var zonas = new ZonaServiceStub(new Zona { Id = 8, Nombre = "Norte" });
        var dimensiones = new DimensionTematicaServiceStub(new DimensionTematica { Id = 3, Nombre = "Tránsito" });
        var organismos = new OrganismoResponsableServiceStub(new OrganismoResponsable { Id = 4, Nombre = "Intendencia" });
        var origenes = new OrigenFinanciamientoServiceStub(new OrigenFinanciamiento { Id = 5, Nombre = "Presupuesto propio" });
        var documentos = new DocumentoServiceFake();

        var svc = new ClasificacionTareaDialogService(
            zonas, dimensiones, organismos, origenes, documentos, new ConfirmacionServiceFake());

        var actual = new DatosClasificacionTarea(ZonaId: 7, DimensionTematicaId: 3,
            OrganismoResponsableId: 4, OrigenFinanciamientoId: 5, DocumentoAdministrativoId: null);

        var task = svc.PedirClasificacionAsync(actual);

        var dialog = (ReclasificarTareaDialog)await EsperarDialogoAsync(owner);
        Dispatcher.UIThread.RunJobs();

        var vm = (ReclasificarTareaDialogViewModel)dialog.DataContext!;

        // La zona 7 (dada de baja) sigue precargada pese a no estar entre las activas -- si
        // esto fallara, el resto del test estaría verificando un desasignado que nunca estuvo
        // asignado, igual que en el test hermano de arriba.
        Assert.Equal(7, vm.Panel.ZonaSeleccionada?.Id);

        // El Admin NO toca el combo de Zona -- solo confirma con "Aceptar". Antes de este fix,
        // InicializarAsync perdía la precarga (ZonaSeleccionada quedaba null) y ObtenerDatos()
        // devolvía ZonaId=null acá, borrando la zona sin que el Admin la tocara.
        var botonAceptar = dialog.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, "Aceptar"));
        Clickear(dialog, botonAceptar);

        var resultado = await EsperarResultadoAsync(task);

        Assert.NotNull(resultado);
        Assert.Equal(7, resultado!.ZonaId);
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

    // ── Fakes privados de este archivo (sin Moq, ver nota de clase) ────────────────────────

    private sealed class ZonaServiceStub : IZonaService
    {
        private readonly List<Zona> _activas;
        public ZonaServiceStub(params Zona[] activas) => _activas = activas.ToList();
        public Task<IReadOnlyList<Zona>> ListarActivasAsync() => Task.FromResult<IReadOnlyList<Zona>>(_activas);
        public Task<int> AltaAsync(Zona zona) => throw new NotSupportedException();
        public Task ModificarAsync(Zona zona) => throw new NotSupportedException();
        public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
        public Task<IReadOnlyList<Zona>> ListarTodasAsync() => throw new NotSupportedException();
    }

    private sealed class DimensionTematicaServiceStub : IDimensionTematicaService
    {
        private readonly List<DimensionTematica> _activas;
        public DimensionTematicaServiceStub(params DimensionTematica[] activas) => _activas = activas.ToList();
        public Task<IReadOnlyList<DimensionTematica>> ListarActivasAsync() => Task.FromResult<IReadOnlyList<DimensionTematica>>(_activas);
        public Task<int> AltaAsync(DimensionTematica dimension) => throw new NotSupportedException();
        public Task ModificarAsync(DimensionTematica dimension) => throw new NotSupportedException();
        public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
        public Task<IReadOnlyList<DimensionTematica>> ListarTodasAsync() => throw new NotSupportedException();
    }

    private sealed class OrganismoResponsableServiceStub : IOrganismoResponsableService
    {
        private readonly List<OrganismoResponsable> _activas;
        public OrganismoResponsableServiceStub(params OrganismoResponsable[] activas) => _activas = activas.ToList();
        public Task<IReadOnlyList<OrganismoResponsable>> ListarActivasAsync() => Task.FromResult<IReadOnlyList<OrganismoResponsable>>(_activas);
        public Task<int> AltaAsync(OrganismoResponsable organismo) => throw new NotSupportedException();
        public Task ModificarAsync(OrganismoResponsable organismo) => throw new NotSupportedException();
        public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganismoResponsable>> ListarTodasAsync() => throw new NotSupportedException();
    }

    private sealed class OrigenFinanciamientoServiceStub : IOrigenFinanciamientoService
    {
        private readonly List<OrigenFinanciamiento> _activas;
        public OrigenFinanciamientoServiceStub(params OrigenFinanciamiento[] activas) => _activas = activas.ToList();
        public Task<IReadOnlyList<OrigenFinanciamiento>> ListarActivasAsync() => Task.FromResult<IReadOnlyList<OrigenFinanciamiento>>(_activas);
        public Task<int> AltaAsync(OrigenFinanciamiento origen) => throw new NotSupportedException();
        public Task ModificarAsync(OrigenFinanciamiento origen) => throw new NotSupportedException();
        public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrigenFinanciamiento>> ListarTodasAsync() => throw new NotSupportedException();
    }
}
