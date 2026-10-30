using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using StockApp.Application.Authorization;
using StockApp.Application.Proyectos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Proyectos;
using StockApp.Presentation.Views.Proyectos;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Gate de la ficha de proyecto con la View REAL (spec 2026-10-30, Review Focus 2): un Operador con proyectos.ver y sin
/// finanzas.ver ve el total gastado, el presupuesto y el disponible, pero no la lista de gastos (la API la manda null).
/// Siempre con permisos mixtos, nunca Admin: el Admin cortocircuita el gate.
/// </summary>
public class ProyectoFormViewTests
{
    private static (Window Ventana, ProyectoFormView Vista, ProyectoFormViewModel Vm) MontarFicha(
        ProyectoServiceFake servicio, int id, SesionFake sesion)
    {
        var vm = new ProyectoFormViewModel(servicio, new TareaServiceFake(), new ZonaServiceFake(), new DimensionTematicaServiceFake(),
            new OrganismoResponsableServiceFake(), new OrigenFinanciamientoServiceFake(), sesion, new NavigationRecorderFake(),
            new ConfirmacionServiceFake());
        vm.CargarParaVer(id);
        var vista = new ProyectoFormView { DataContext = vm };
        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return (ventana, vista, vm);
    }

    private static T Parte<T>(Control vista, string nombre) where T : Control => V2.MontajeV2.Parte<T>(vista, nombre);

    private static bool Visible(Control c) => ArbolVisual.EsVisibleEnArbol(c);

    private static ProyectoServiceFake ServicioConGastos(decimal? presupuesto)
    {
        var servicio = new ProyectoServiceFake(new List<Proyecto>
        {
            new() { Id = 4, Titulo = "Rambla", Estado = EstadoProyecto.EnCurso, Presupuesto = presupuesto },
        });
        servicio.GastosPorProyecto[4] = new List<GastoDeProyectoDto>
        {
            new(10, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), "Barraca Sur", "Arena gruesa", 400m),
            new(11, new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), "Ferretería Centro", "Cemento", 600m),
        };
        return servicio;
    }

    [AvaloniaFact]
    public void FichaProyecto_SinVerFinanzas_ListaDeGastosNoVisible_TotalesVisibles()
    {
        var servicio = ServicioConGastos(5000m);
        servicio.ConVerFinanzas = false;

        var (_, vista, _) = MontarFicha(servicio, 4, new SesionFake(RolUsuario.Operador, Permisos.VerProyectos, Permisos.GestionarTareas));

        Assert.False(Visible(Parte<Control>(vista, "SeccionListaGastos")));
        Assert.False(Visible(Parte<DataGrid>(vista, "GrillaGastos")));
        Assert.True(Visible(Parte<TextBlock>(vista, "ValorTotalGastado")));
        Assert.Equal("$ 1.000,00", Parte<TextBlock>(vista, "ValorTotalGastado").Text);
        Assert.True(Visible(Parte<TextBlock>(vista, "ValorPresupuesto")));
        Assert.Equal("$ 5.000,00", Parte<TextBlock>(vista, "ValorPresupuesto").Text);
        Assert.True(Visible(Parte<TextBlock>(vista, "ValorDisponible")));
        Assert.Equal("$ 4.000,00", Parte<TextBlock>(vista, "ValorDisponible").Text);
    }

    [AvaloniaFact]
    public void FichaProyecto_ConVerFinanzas_MuestraLaListaDeGastos()
    {
        var (_, vista, _) = MontarFicha(ServicioConGastos(5000m), 4,
            new SesionFake(RolUsuario.Operador, Permisos.VerProyectos, Permisos.VerFinanzas));

        var grilla = Parte<DataGrid>(vista, "GrillaGastos");
        Assert.True(Visible(grilla));
        Assert.Equal(new[] { "FECHA", "PROVEEDOR", "DETALLE", "MONTO" }, grilla.Columns.Select(c => c.Header as string).ToArray());
        Assert.Equal(2, grilla.ItemsSource!.Cast<object>().Count());
    }

    [AvaloniaFact]
    public void FichaProyecto_SinPresupuesto_SoloTotalGastado()
    {
        var (_, vista, _) = MontarFicha(ServicioConGastos(null), 4,
            new SesionFake(RolUsuario.Operador, Permisos.VerProyectos, Permisos.VerFinanzas));

        Assert.True(Visible(Parte<TextBlock>(vista, "ValorTotalGastado")));
        Assert.False(Visible(Parte<Control>(vista, "CampoPresupuestoFicha")));
        Assert.False(Visible(Parte<Control>(vista, "CampoDisponible")));
        Assert.False(Visible(Parte<Control>(vista, "CampoSobregiro")));
    }

    [AvaloniaFact]
    public void FichaProyecto_Sobregiro_EnRojoConElValorAbsoluto()
    {
        var (_, vista, _) = MontarFicha(ServicioConGastos(0m), 4,
            new SesionFake(RolUsuario.Operador, Permisos.VerProyectos, Permisos.VerFinanzas));

        var sobregiro = Parte<TextBlock>(vista, "ValorSobregiro");
        Assert.True(Visible(sobregiro));
        Assert.Equal("$ 1.000,00", sobregiro.Text);
        Assert.Contains("negativo", sobregiro.Classes);
        Assert.False(Visible(Parte<Control>(vista, "CampoDisponible")));
    }
}
