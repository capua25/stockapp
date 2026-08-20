using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Documentos;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Red de seguridad de los 5 gates de fila de DocumentoListView.axaml -- Task 9.0 del plan de
/// Fase B, cierra la deuda de las Tasks 4.3/4.4 de la Fase A (Ruling B-5). El brief original de
/// esas tasks pedía "usuario de permisos mixtos": NO aplica -- ninguna de las 5 fórmulas lee
/// PermisosActuales. Tres son puro autómata de estado (DocumentoAdministrativo.PuedeTransicionarA)
/// y dos comparan el rol contra Admin a secas (PuedeAnular, PuedeReabrir). La matriz real es
/// rol x estado, resuelta contra DocumentoAdministrativo.TransicionesValidas.
///
/// Fórmulas (DocumentoListViewModel.cs, clase DocumentoFila):
/// - PuedeIniciar          => Estado == Pendiente && PuedeTransicionarA(EnProceso)
/// - PuedeVolverAPendiente => PuedeTransicionarA(Pendiente)   -- true solo en EnProceso
/// - PuedeFinalizar        => PuedeTransicionarA(Finalizado)  -- true solo en EnProceso (MISMA
///   condición que PuedeVolverAPendiente: se distinguen por botón, no por estado)
/// - PuedeAnular  => rol == Admin && PuedeTransicionarA(Anulado)   -- true en Pendiente/EnProceso
/// - PuedeReabrir => rol == Admin && EsCerrado                     -- true en Finalizado/Anulado
///
/// Los 5 gates viven dentro de ItemsControl.ItemTemplate (Activos: PuedeIniciar/VolverAPendiente/
/// Finalizar/Anular; Historial: PuedeReabrir), así que hace falta montar con UN documento real en
/// la colección correspondiente para que el control se realice. PuedeReabrir vive en la solapa
/// Historial (TabItem no seleccionado no realiza su contenido en headless): los casos 6-8
/// seleccionan la solapa antes de buscar el botón.
/// </summary>
public class DocumentoListViewGatesTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:docs="clr-namespace:StockApp.Presentation.Views.Documentos;assembly=GestionMunicipal"
                Width="1100" Height="800">
            <docs:DocumentoListView />
        </Window>
        """;

    private static DocumentoAdministrativo DocumentoDe(int id, EstadoDocumento estado) => new()
    {
        Id = id, Numero = "0001", Anio = 2026, Tipo = TipoDocumento.Expediente,
        FechaEmision = DateTime.UtcNow.Date, Descripcion = "Descripción",
        Estado = estado, RegistradoPorUsuarioId = 1, FechaRegistro = DateTime.UtcNow,
    };

    private static (Window Window, DocumentoListViewModel Vm) Montar(
        RolUsuario rol, List<DocumentoAdministrativo>? activos = null, List<DocumentoAdministrativo>? historial = null)
    {
        var servicio = new DocumentoServiceFake(activos, historial);
        var vm = new DocumentoListViewModel(
            servicio, new SesionFake(rol), new NavigationRecorderDocumentosFake(), new ConfirmacionServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        return (window, vm);
    }

    private static void SeleccionarSolapaHistorial(Window window)
    {
        var tabControl = window.GetVisualDescendants().OfType<TabControl>().First();
        tabControl.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await AbrirHistorialCommand
    }

    private static Button BotonPorContenido(Window window, string texto)
        => window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == texto);

    // ---- Caso 1: Operador + Pendiente (Activos) ----

    [AvaloniaFact]
    public void PuedeIniciar_OperadorPendiente_IniciarVisibleYRestoOculto()
    {
        var (window, _) = Montar(RolUsuario.Operador, activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Pendiente),
        });

        Assert.True(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Iniciar")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Volver a pendiente")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Finalizar")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Anular…")));
    }

    // ---- Caso 2: Operador + EnProceso (Activos) ----
    // PuedeVolverAPendiente y PuedeFinalizar son la MISMA condición (Estado == EnProceso): se
    // distinguen por botón, no por estado -- este test cubre a los dos juntos.

    [AvaloniaFact]
    public void PuedeVolverAPendienteYFinalizar_OperadorEnProceso_VisiblesYRestoOculto()
    {
        var (window, _) = Montar(RolUsuario.Operador, activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.EnProceso),
        });

        Assert.True(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Volver a pendiente")));
        Assert.True(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Finalizar")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Iniciar")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Anular…")));
    }

    // ---- Casos 3-5: PuedeAnular (Admin vs. Operador, Pendiente vs. EnProceso) ----

    [AvaloniaFact]
    public void PuedeAnular_AdminPendiente_Visible()
    {
        var (window, _) = Montar(RolUsuario.Admin, activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Pendiente),
        });

        Assert.True(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Anular…")));
    }

    /// <summary>Caso que NO existía antes de la Task 9.0: DocumentoListViewTests.cs solo montaba
    /// con Admin, así que PuedeAnular nunca se veía en false. Es el único caso que prueba el gate
    /// de rol de verdad (sin él, un Operador vería "Anular…" y comería el 403 del servidor).</summary>
    [AvaloniaFact]
    public void PuedeAnular_OperadorPendiente_Oculto()
    {
        var (window, _) = Montar(RolUsuario.Operador, activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Pendiente),
        });

        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Anular…")));
    }

    [AvaloniaFact]
    public void PuedeAnular_AdminEnProceso_Visible()
    {
        var (window, _) = Montar(RolUsuario.Admin, activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.EnProceso),
        });

        Assert.True(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Anular…")));
    }

    // ---- Casos 6-8: PuedeReabrir (Historial, Admin vs. Operador, Finalizado vs. Anulado) ----

    [AvaloniaFact]
    public void PuedeReabrir_AdminFinalizado_Visible()
    {
        var (window, _) = Montar(RolUsuario.Admin, historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Finalizado),
        });
        SeleccionarSolapaHistorial(window);

        Assert.True(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Reabrir…")));
    }

    /// <summary>Segundo caso que NO existía antes de la Task 9.0: el único que prueba que
    /// PuedeReabrir también apaga el botón para un Operador (documento cerrado).</summary>
    [AvaloniaFact]
    public void PuedeReabrir_OperadorFinalizado_Oculto()
    {
        var (window, _) = Montar(RolUsuario.Operador, historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Finalizado),
        });
        SeleccionarSolapaHistorial(window);

        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Reabrir…")));
    }

    [AvaloniaFact]
    public void PuedeReabrir_AdminAnulado_Visible()
    {
        var (window, _) = Montar(RolUsuario.Admin, historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Anulado),
        });
        SeleccionarSolapaHistorial(window);

        Assert.True(ArbolVisual.EsVisibleEnArbol(BotonPorContenido(window, "Reabrir…")));
    }
}
