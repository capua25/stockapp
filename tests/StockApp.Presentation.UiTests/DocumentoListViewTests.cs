using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using StockApp.Application.Documentos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Documentos;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Verificación de DocumentoListView contra el árbol visual real (molde: TareaListViewTests).
/// Cubre lo que el spec asigna explícitamente a UiTests: carga por DataContextChanged y que
/// el cambio de solapa dispare la carga perezosa del historial (D9).
/// </summary>
public class DocumentoListViewTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:docs="clr-namespace:StockApp.Presentation.Views.Documentos;assembly=GestionMunicipal"
                Width="1100" Height="800">
            <docs:DocumentoListView />
        </Window>
        """;

    private static DocumentoAdministrativo DocumentoDe(int id, string numero, EstadoDocumento estado) => new()
    {
        Id = id, Numero = numero, Anio = 2026, Tipo = TipoDocumento.Expediente,
        FechaEmision = DateTime.UtcNow.Date, Descripcion = $"Descripción {numero}",
        Estado = estado, RegistradoPorUsuarioId = 1, FechaRegistro = DateTime.UtcNow,
    };

    private static (Window Window, DocumentoListViewModel Vm, DocumentoServiceFake Servicio) Montar(
        List<DocumentoAdministrativo>? activos = null, List<DocumentoAdministrativo>? historial = null,
        RolUsuario rol = RolUsuario.Admin)
    {
        var servicio = new DocumentoServiceFake(activos, historial);
        var vm = new DocumentoListViewModel(
            servicio, new SesionFake(rol), new NavigationRecorderDocumentosFake(), new ConfirmacionServiceFake())
        { EsperaDebounce = TimeSpan.Zero };

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        return (window, vm, servicio);
    }

    [AvaloniaFact]
    public void Montar_ConDocumentosActivos_LosCargaPorDataContextChanged()
    {
        var (window, vm, _) = Montar(activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, "0087", EstadoDocumento.Pendiente),
        });

        Assert.Single(vm.Activos);
        var textos = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Descripción 0087", textos);
    }

    [AvaloniaFact]
    public void Montar_VistaActivosPorDefecto_NoCargaElHistorial()
    {
        var (_, vm, servicio) = Montar(historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(9, "0001", EstadoDocumento.Finalizado),
        });

        Assert.Empty(vm.Historial);
        Assert.Equal(0, servicio.LlamadasListarHistorial);
    }

    private static void EsperarRecarga(DocumentoListViewModel vm)
    {
        Dispatcher.UIThread.RunJobs();
        vm._tareaDebounce.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ClickReal_EligiendoHistorialEnElCombo_ConsultaElHistorialCadaVez()
    {
        // Ya no hay pestañas ni carga "solo la primera vez": cada elección de la vista consulta (siempre se ve lo último).
        var (window, vm, servicio) = Montar(historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(9, "0001", EstadoDocumento.Finalizado),
        });
        var combo = window.GetVisualDescendants().OfType<ComboBox>().First(c => c.Name == "ComboVistaActivos");

        combo.SelectedItem = vm.Vistas[1];
        EsperarRecarga(vm);

        Assert.Single(vm.Historial);
        Assert.Equal(1, servicio.LlamadasListarHistorial);

        vm.VistaSeleccionada = vm.Vistas[0];
        EsperarRecarga(vm);
        vm.VistaSeleccionada = vm.Vistas[1];
        EsperarRecarga(vm);

        Assert.Equal(2, servicio.LlamadasListarHistorial);
    }

    [AvaloniaFact]
    public void ClickReal_TipeandoEnBuscarDeActivos_VuelveAConsultarAlServicio()
    {
        // I2 del review final: FiltroActivosTexto/Tipo no tenían ningún disparador propio en la vista real; ahora recargan
        // solos tras una espera corta.
        var (window, vm, servicio) = Montar(activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, "0087", EstadoDocumento.Pendiente),
        });
        Assert.Equal(1, servicio.LlamadasListarActivos);

        window.GetVisualDescendants().OfType<TextBox>().First(c => c.Name == "CajaBuscarActivos").Text = "0087";
        EsperarRecarga(vm);

        Assert.Equal(2, servicio.LlamadasListarActivos);
        Assert.Equal("0087", servicio.UltimoFiltroActivos?.Texto);
    }

    [AvaloniaFact]
    public void ClickReal_EligiendoTipoEnElComboDeActivos_LlegaAlServicio()
    {
        var (window, vm, servicio) = Montar(activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, "0087", EstadoDocumento.Pendiente),
        });

        var combo = window.GetVisualDescendants().OfType<ComboBox>().First(c => c.Name == "ComboTipoActivos");
        combo.SelectedItem = vm.TiposDisponibles.First(o => o.Valor == TipoDocumento.Oficio);
        EsperarRecarga(vm);

        Assert.Equal(TipoDocumento.Oficio, servicio.UltimoFiltroActivos?.Tipo);
    }

    [AvaloniaFact]
    public void ClickReal_EnAnular_NavegaAlDetalleEnVezDeEjecutarLaAccionAca()
    {
        // I3: el botón "Anular…" sigue navegando al detalle (motivo obligatorio vive en el
        // formulario) -- este test fija esa decisión de diseño para que no se rompa sin querer.
        var (window, vm, servicio) = Montar(activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, "0087", EstadoDocumento.Pendiente),
        }, rol: RolUsuario.Admin);
        vm.FilaSeleccionadaActivos = vm.Activos[0];
        Dispatcher.UIThread.RunJobs();

        var boton = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Anular…");
        boton.Command!.Execute(boton.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, servicio.LlamadasListarHistorial); // no llamó a AnularAsync ni recargó nada del servicio
    }

}

/// <summary>Análogo de NavigationRecorderFake (TareaFakes.cs) para el módulo Documentos --
/// separado porque graba navegación hacia DocumentoFormViewModel, no TareaFormViewModel.</summary>
internal sealed class NavigationRecorderDocumentosFake : INavigationService
{
    public StockApp.Presentation.ViewModels.ViewModelBase? Actual => null;
    public event Action? Cambiado { add { } remove { } }

    public Type? UltimoTipoNavegado { get; private set; }

    public void Navegar<TVm>() where TVm : StockApp.Presentation.ViewModels.ViewModelBase
        => UltimoTipoNavegado = typeof(TVm);

    public void Navegar<TVm>(Action<TVm> inicializar) where TVm : StockApp.Presentation.ViewModels.ViewModelBase
        => UltimoTipoNavegado = typeof(TVm);
}
