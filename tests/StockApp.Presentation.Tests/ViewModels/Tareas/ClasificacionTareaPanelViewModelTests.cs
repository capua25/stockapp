using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Tareas;

public class ClasificacionTareaPanelViewModelTests
{
    private static (ClasificacionTareaPanelViewModel Vm, Mock<IZonaService> Zonas,
                     Mock<IDimensionTematicaService> Dimensiones, Mock<IOrganismoResponsableService> Organismos,
                     Mock<IOrigenFinanciamientoService> Origenes, Mock<IDocumentoAdministrativoService> Documentos)
        Crear()
    {
        var zonas = new Mock<IZonaService>();
        var dimensiones = new Mock<IDimensionTematicaService>();
        var organismos = new Mock<IOrganismoResponsableService>();
        var origenes = new Mock<IOrigenFinanciamientoService>();
        var documentos = new Mock<IDocumentoAdministrativoService>();

        zonas.Setup(z => z.ListarActivasAsync()).ReturnsAsync(new List<Zona>
        {
            new() { Id = 1, Nombre = "Centro" }, new() { Id = 2, Nombre = "Norte" },
        });
        dimensiones.Setup(d => d.ListarActivasAsync()).ReturnsAsync(new List<DimensionTematica>
        {
            new() { Id = 3, Nombre = "Tránsito" },
        });
        organismos.Setup(o => o.ListarActivasAsync()).ReturnsAsync(new List<OrganismoResponsable>
        {
            new() { Id = 4, Nombre = "Intendencia" },
        });
        origenes.Setup(o => o.ListarActivasAsync()).ReturnsAsync(new List<OrigenFinanciamiento>
        {
            new() { Id = 5, Nombre = "Presupuesto propio" },
        });

        var vm = new ClasificacionTareaPanelViewModel(
            zonas.Object, dimensiones.Object, organismos.Object, origenes.Object, documentos.Object);
        return (vm, zonas, dimensiones, organismos, origenes, documentos);
    }

    [Fact]
    public async Task InicializarAsync_SinValoresActuales_PopulaLosCuatroCatalogosSinSeleccion()
    {
        var ctx = Crear();

        await ctx.Vm.InicializarAsync();

        Assert.Equal(2, ctx.Vm.ZonasDisponibles.Count);
        Assert.Single(ctx.Vm.DimensionesDisponibles);
        Assert.Single(ctx.Vm.OrganismosDisponibles);
        Assert.Single(ctx.Vm.OrigenesDisponibles);
        Assert.Null(ctx.Vm.ZonaSeleccionada);
        Assert.Null(ctx.Vm.DocumentoSeleccionado);
    }

    [Fact]
    public async Task InicializarAsync_ConValoresActuales_PrecargaLaSeleccion()
    {
        var ctx = Crear();
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(9)).ReturnsAsync(new DocumentoAdministrativo
        {
            Id = 9, Numero = "0001", Anio = 2026, Tipo = TipoDocumento.Expediente, Descripcion = "x",
        });

        await ctx.Vm.InicializarAsync(new DatosClasificacionTarea(1, 3, null, null, 9));

        Assert.Equal(1, ctx.Vm.ZonaSeleccionada?.Id);
        Assert.Equal(3, ctx.Vm.DimensionSeleccionada?.Id);
        Assert.Null(ctx.Vm.OrganismoSeleccionado);
        Assert.Equal(9, ctx.Vm.DocumentoSeleccionado?.Id);
    }

    [Fact]
    public void ObtenerDatos_SinSeleccion_DevuelveLosCincoCamposEnNull()
    {
        var ctx = Crear();

        var datos = ctx.Vm.ObtenerDatos();

        Assert.Equal(new DatosClasificacionTarea(null, null, null, null, null), datos);
    }

    [Fact]
    public async Task ObtenerDatos_ConSeleccion_ReflejaLosIdsSeleccionados()
    {
        var ctx = Crear();
        await ctx.Vm.InicializarAsync();
        ctx.Vm.ZonaSeleccionada = ctx.Vm.ZonasDisponibles.First(z => z.Id == 2);

        var datos = ctx.Vm.ObtenerDatos();

        Assert.Equal(2, datos.ZonaId);
        Assert.Null(datos.DimensionTematicaId);
    }

    [Fact]
    public async Task BuscarExpedientesAsync_TextoCortoDelMinimo_NoConsultaAlServicio()
    {
        var ctx = Crear();

        var resultado = await ctx.Vm.BuscarExpedientesAsync("ab", CancellationToken.None);

        Assert.Empty(resultado);
        ctx.Documentos.Verify(d => d.ListarActivosAsync(It.IsAny<FiltroDocumentos>()), Times.Never);
    }

    [Fact]
    public async Task BuscarExpedientesAsync_TextoValido_FiltraPorTipoExpediente()
    {
        var ctx = Crear();
        ctx.Documentos.Setup(d => d.ListarActivosAsync(It.IsAny<FiltroDocumentos>()))
            .ReturnsAsync(new List<DocumentoAdministrativo>
            {
                new() { Id = 1, Numero = "0001", Tipo = TipoDocumento.Expediente, Descripcion = "x" },
            });

        var resultado = await ctx.Vm.BuscarExpedientesAsync("obra", CancellationToken.None);

        Assert.Single(resultado);
        ctx.Documentos.Verify(d => d.ListarActivosAsync(
            It.Is<FiltroDocumentos>(f => f.Tipo == TipoDocumento.Expediente && f.Texto == "obra")), Times.Once);
    }

    [Fact]
    public async Task BuscarExpedientesAsync_MasResultadosQueElTope_AcotaYAvisa()
    {
        var ctx = Crear();
        var muchos = Enumerable.Range(1, 25)
            .Select(i => new DocumentoAdministrativo { Id = i, Numero = $"{i:0000}", Tipo = TipoDocumento.Expediente, Descripcion = "x" })
            .ToList();
        ctx.Documentos.Setup(d => d.ListarActivosAsync(It.IsAny<FiltroDocumentos>())).ReturnsAsync(muchos);

        var resultado = await ctx.Vm.BuscarExpedientesAsync("expediente", CancellationToken.None);

        Assert.Equal(20, resultado.Count());
        Assert.NotNull(ctx.Vm.MensajeBuscadorExpediente);
    }

    [Fact]
    public async Task BuscarExpedientesAsync_SinPermisoDeDocumentos_DegradaSinRomperElRestoDelPanel()
    {
        var ctx = Crear();
        await ctx.Vm.InicializarAsync();
        ctx.Vm.ZonaSeleccionada = ctx.Vm.ZonasDisponibles.First(z => z.Id == 2);
        ctx.Documentos.Setup(d => d.ListarActivosAsync(It.IsAny<FiltroDocumentos>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        var resultado = await ctx.Vm.BuscarExpedientesAsync("obra", CancellationToken.None);

        Assert.Empty(resultado);
        Assert.NotNull(ctx.Vm.MensajeBuscadorExpediente);

        var datos = ctx.Vm.ObtenerDatos();
        Assert.Equal(2, datos.ZonaId);
        Assert.Null(datos.DocumentoAdministrativoId);
    }
}
