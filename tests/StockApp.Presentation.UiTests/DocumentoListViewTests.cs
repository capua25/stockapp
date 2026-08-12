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
                xmlns:docs="clr-namespace:StockApp.Presentation.Views.Documentos;assembly=StockApp.Presentation"
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
            servicio, new TareaSessionFake(rol), new NavigationRecorderDocumentosFake(), new ConfirmacionServiceFake());

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
    public void Montar_SolapaActivosSeleccionadaPorDefecto_NoCargaElHistorial()
    {
        var (_, vm, servicio) = Montar(historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(9, "0001", EstadoDocumento.Finalizado),
        });

        Assert.Empty(vm.Historial);
        Assert.Equal(0, servicio.LlamadasListarHistorial);
    }

    [AvaloniaFact]
    public void ClickReal_EnLaSolapaHistorial_DisparaLaCargaPerezosa()
    {
        var (window, vm, servicio) = Montar(historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(9, "0001", EstadoDocumento.Finalizado),
        });

        var tabControl = window.GetVisualDescendants().OfType<TabControl>().First();
        tabControl.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.Single(vm.Historial);
        Assert.Equal(1, servicio.LlamadasListarHistorial);

        // Volver a Activos y de nuevo a Historial no debe repetir la consulta (carga perezosa
        // = una sola vez, no "cada vez que se selecciona").
        tabControl.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        tabControl.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, servicio.LlamadasListarHistorial);
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
