using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Monta LibroCajaView real (mismo patrón que GastosViewTests.cs: VM real + fakes hechos a mano,
/// sin Moq porque este proyecto no lo referencia) para custodiar el bugfix 2026-08-19: los dos
/// NumericUpDown iniciales de la barra de filtros (bindeados a Anio y Mes en LibroCajaViewModel)
/// no tenían ninguna etiqueta -- el usuario reportó que tenía que "asumir" qué eran. Este test
/// verifica que existan los TextBlock Classes="caption" con el texto correcto encima de cada
/// campo, siguiendo el mismo patrón que MovimientoHistorialView (filtros "Tipo"/"Desde"/"Hasta").
/// </summary>
public class LibroCajaViewTests
{
    private sealed class FinanzasVistasServiceFake : IFinanzasVistasService
    {
        public Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes)
            => Task.FromResult(new LibroCajaMesDto(
                anio, mes, 0m, 0m,
                Array.Empty<MovimientoCajaDto>(), Array.Empty<TotalPorClaveDto>(), Array.Empty<TotalPorClaveDto>()));

        public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio)
            => Task.FromResult(new LibroCajaAnualDto(anio, Array.Empty<TotalMensualDto>(), Array.Empty<TotalPorClaveDto>()));

        public Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    private sealed class CsvExporterFake : ICsvExporter
    {
        public string Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnOrder) => "csv";
    }

    private sealed class ServicioGuardadoArchivoFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarTextoAsync(string contenido, string nombreSugerido) => Task.FromResult(true);
        public Task<bool> GuardarBytesAsync(Stream contenido, string nombreSugerido, CancellationToken ct = default) => Task.FromResult(true);
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=StockApp.Presentation"
                Width="1100" Height="700">
            <vistas:LibroCajaView />
        </Window>
        """;

    private static Window Montar()
    {
        var vm = new LibroCajaViewModel(
            new FinanzasVistasServiceFake(),
            new CsvExporterFake(),
            new ServicioGuardadoArchivoFake(),
            new ConfirmacionServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        return window;
    }

    private static TextBlock BuscarEtiquetaCaptionPorTexto(Window window, string texto)
        => window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Classes.Contains("caption") && t.Text == texto);

    [AvaloniaFact]
    public void Montar_MuestraEtiquetaAnioSobreElCampo()
    {
        var window = Montar();

        var etiqueta = BuscarEtiquetaCaptionPorTexto(window, "Año");

        Assert.True(etiqueta.IsVisible);
    }

    [AvaloniaFact]
    public void Montar_MuestraEtiquetaMesSobreElCampo()
    {
        var window = Montar();

        var etiqueta = BuscarEtiquetaCaptionPorTexto(window, "Mes");

        Assert.True(etiqueta.IsVisible);
    }
}
