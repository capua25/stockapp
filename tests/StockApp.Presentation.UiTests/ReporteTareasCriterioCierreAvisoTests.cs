using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Auth;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.Views.Reportes;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardián del aviso de <c>ReporteTareasView.axaml</c> (D16): con criterio "Fecha de cierre",
/// el <c>TextBlock</c> explica que las columnas Pendientes/En curso dan cero A PROPÓSITO, no
/// por falta de datos.
///
/// Hallazgo de la revisión final del Plan C: el reviewer borró el bloque entero del XAML y la
/// suite de UiTests siguió 533/533 verde -- ningún test montaba la View real con el criterio
/// en Cierre. El test de ViewModel existente
/// (<c>ReporteTareasViewModelTests.EsCriterioCierre_AlPonerloEnTrue_...</c>) prueba la
/// propiedad, no lo que el XAML hace con ella -- mismo caso de "un test de VM no custodia un
/// gate de UI" ya documentado en el proyecto.
///
/// Verificado por mutación: sacando el <c>TextBlock</c> del aviso,
/// <see cref="AvisoVisible_ConCriterioCierre"/> se pone rojo; se revirtió después de confirmar.
/// </summary>
public class ReporteTareasCriterioCierreAvisoTests
{
    private const string TextoAviso =
        "Criterio 'Fecha de cierre': solo se cuentan tareas Terminadas o Canceladas. " +
        "Las columnas 'Pendientes' y 'En curso' muestran 0 a propósito, no faltan datos.";

    // Fake mínimo: no hay uno compartido para IReporteTareasService en este proyecto (a
    // diferencia de Presentation.Tests, que usa Moq -- UiTests no referencia Moq). El único
    // rol acá es dejar que DataContextChanged dispare CargarAsync sin explotar; el contenido
    // del reporte no importa para este gate de visibilidad.
    private sealed class ReporteTareasServiceFake : IReporteTareasService
    {
        public Task<ReporteTareasDto> ObtenerAsync(FiltroReporteTareas filtro) =>
            Task.FromResult(new ReporteTareasDto(new List<FilaReporteTareas>(), 0));
    }

    // Fakes mínimos no-op para las 6 dependencias nuevas del constructor (spec 2026-09-18):
    // este proyecto no referencia Moq (a diferencia de Presentation.Tests).
    private sealed class CsvExporterFake : ICsvExporter
    {
        public string Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnOrder) => string.Empty;
    }

    private sealed class PdfExporterFake : IPdfExporter
    {
        public byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<ColumnaPdf> columnas, MetadatosDocumento metadatos, ResumenPdf? resumen = null) => Array.Empty<byte>();
    }

    private sealed class GuardadoFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarTextoAsync(string contenido, string nombreSugerido) => Task.FromResult(false);
        public Task<bool> GuardarBytesAsync(
            Stream contenido, string nombreSugerido, CancellationToken ct = default,
            string? extension = null, string? tipoMime = null) => Task.FromResult(false);
    }

    private sealed class AperturaFake : IServicioAperturaArchivo
    {
        public Task AbrirAsync(string nombreArchivo, byte[] contenido) => Task.CompletedTask;
    }

    private sealed class ConfirmacionFake : IConfirmacionService
    {
        public Task<bool> PreguntarAsync(string mensaje) => Task.FromResult(false);
        public Task InformarAsync(string mensaje) => Task.CompletedTask;
        public Task<string?> PedirTextoAsync(string titulo, string mensaje) => Task.FromResult<string?>(null);
    }

    private sealed class SessionFake : ICurrentSession
    {
        public bool EstaAutenticado => true;
        public UsuarioSesion? UsuarioActual => new(1, "admin", RolUsuario.Admin, null);
        public RolUsuario? RolActual => RolUsuario.Admin;
        public IReadOnlySet<string> PermisosActuales => new HashSet<string>();
        public void EstablecerPermisos(IReadOnlySet<string> permisos) { }
        public void IniciarSesion(Usuario usuario) { }
        public void CerrarSesion() { }
    }

    private static (Window Window, ReporteTareasViewModel Vm) Montar()
    {
        var vm = new ReporteTareasViewModel(
            new ReporteTareasServiceFake(), new CsvExporterFake(), new PdfExporterFake(),
            new GuardadoFake(), new AperturaFake(), new ConfirmacionFake(), new SessionFake());
        var vista = new ReporteTareasView();
        var window = new Window { Width = 1100, Height = 700, Content = vista };

        window.DataContext = vm; // dispara CargarAsync vía DataContextChanged (ReporteTareasView.axaml.cs)
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        return (window, vm);
    }

    private static TextBlock AvisoTextBlock(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == TextoAviso);

    [AvaloniaFact]
    public void AvisoVisible_ConCriterioCierre()
    {
        var (window, vm) = Montar();

        vm.EsCriterioCierre = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(ArbolVisual.EsVisibleEnArbol(AvisoTextBlock(window)));
    }

    [AvaloniaFact]
    public void AvisoOculto_ConCriterioCreacion()
    {
        var (window, vm) = Montar();

        // Default del constructor ya es Creación (D16) -- se reafirma explícito por claridad
        // y para no depender de un detalle de implementación que podría cambiar.
        vm.EsCriterioCreacion = true;
        Dispatcher.UIThread.RunJobs();

        Assert.False(ArbolVisual.EsVisibleEnArbol(AvisoTextBlock(window)));
    }
}
