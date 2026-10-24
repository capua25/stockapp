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
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Monta LibroCajaView real (mismo patrón que GastosViewTests.cs: VM real + fakes hechos a mano,
/// sin Moq porque este proyecto no lo referencia) para custodiar el bugfix 2026-08-19: los dos
/// NumericUpDown iniciales de la barra de filtros (bindeados a Anio y Mes en LibroCajaViewModel)
/// no tenían ninguna etiqueta -- el usuario reportó que tenía que "asumir" qué eran. Este test
/// verifica que cada NumericUpDown esté dentro de un CampoFormulario v2 con la etiqueta correcta
/// (en la v1 era un TextBlock Classes="caption" encima del campo).
/// </summary>
public class LibroCajaViewTests
{
    private sealed class FinanzasVistasServiceFake : IFinanzasVistasService
    {
        public Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(Caja caja, int anio, int mes)
            => Task.FromResult(new LibroCajaMesDto(
                anio, mes, 0m, 0m,
                Array.Empty<MovimientoCajaDto>(), Array.Empty<TotalPorClaveDto>(), Array.Empty<TotalPorClaveDto>()));

        public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(Caja caja, int anio)
            => Task.FromResult(new LibroCajaAnualDto(anio, Array.Empty<TotalMensualDto>(), Array.Empty<TotalPorClaveDto>()));

        public Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    private sealed class ServicioGuardadoArchivoFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarBytesAsync(
            Stream contenido, string nombreSugerido, CancellationToken ct = default,
            string? extension = null, string? tipoMime = null) => Task.FromResult(true);
    }

    private sealed class PdfExporterNoOpFake : IPdfExporter
    {
        public byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<ColumnaPdf> columnas, MetadatosDocumento metadatos, ResumenPdf? resumen = null)
            => Array.Empty<byte>();
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=GestionMunicipal"
                Width="1100" Height="700">
            <vistas:LibroCajaView />
        </Window>
        """;

    private static Window Montar()
    {
        var vm = new LibroCajaViewModel(
            new FinanzasVistasServiceFake(),
            new ServicioGuardadoArchivoFake(),
            new ConfirmacionServiceFake(),
            new PdfExporterNoOpFake(),
            new ServicioAperturaArchivoFake(),
            new SesionFake(RolUsuario.Admin));

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        return window;
    }

    /// <summary>v2: la etiqueta la pone el CampoFormulario que envuelve al NumericUpDown (antes, un TextBlock caption).</summary>
    private static StockApp.Presentation.Controls.V2.CampoFormulario BuscarCampoPorEtiqueta(Window window, string texto)
        => window.GetVisualDescendants().OfType<StockApp.Presentation.Controls.V2.CampoFormulario>()
            .First(c => c.Etiqueta == texto);

    [AvaloniaFact]
    public void Montar_MuestraEtiquetaAnioSobreElCampo()
    {
        var window = Montar();

        var campo = BuscarCampoPorEtiqueta(window, "Año");

        Assert.True(ArbolVisual.EsVisibleEnArbol(campo));
        Assert.Contains(campo.GetVisualDescendants().OfType<NumericUpDown>(), n => ArbolVisual.EsVisibleEnArbol(n));
    }

    [AvaloniaFact]
    public void Montar_MuestraEtiquetaMesSobreElCampo()
    {
        var window = Montar();

        var campo = BuscarCampoPorEtiqueta(window, "Mes");

        Assert.True(ArbolVisual.EsVisibleEnArbol(campo));
        Assert.Contains(campo.GetVisualDescendants().OfType<NumericUpDown>(), n => ArbolVisual.EsVisibleEnArbol(n));
    }

    /// <summary>Bug de integridad 2026-10-01: "13" en Mes no marcaba nada, el ViewModel seguía con
    /// el mes anterior y Actualizar consultaba ese mes viejo. Ver NumericUpDownValidacionBehavior.</summary>
    [AvaloniaTheory]
    [InlineData("13")]
    [InlineData("abc")]
    public void Mes_Invalido_MarcaElError_BloqueaActualizar_YNoVuelveAlValorAnterior(string texto)
    {
        var window = Montar();
        var vm = (LibroCajaViewModel)window.DataContext!;
        var mesAnterior = vm.Mes;
        var actualizar = NumericUpDownPrueba.BotonPorCommand(window, vm.RecargarCommand);
        var mes = window.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Maximum == 12);
        Assert.True(actualizar.IsEffectivelyEnabled);

        NumericUpDownPrueba.Tipear(mes, texto);

        NumericUpDownPrueba.AssertErrorVisible(mes, "Ingresá un mes válido, entre 1 y 12.");
        NumericUpDownPrueba.AssertBloqueadoConTooltip(actualizar);
        Assert.Equal(mesAnterior, vm.Mes);

        var anio = window.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Maximum == 2100);
        NumericUpDownPrueba.Interno(anio).Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(texto, NumericUpDownPrueba.Interno(mes).Text);
        NumericUpDownPrueba.AssertBloqueadoConTooltip(actualizar);
    }
}
