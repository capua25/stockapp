using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Proyectos;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class ProyectoApiClientTests
{
    private static object ProyectoJson(int id = 3, string titulo = "Plaza nueva") => new
    {
        id, titulo, descripcion = "con juegos",
        estado = EstadoProyecto.EnCurso, prioridad = PrioridadTarea.Alta,
        fechaLimite = new DateTime(2026, 12, 10, 15, 0, 0, DateTimeKind.Utc), presupuesto = 5000m,
        creadoPorUsuarioId = 1, fechaCreacion = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
        fechaFin = (DateTime?)null,
        zonaId = 4, zonaNombre = "Centro",
        dimensionTematicaId = (int?)null, dimensionTematicaNombre = (string?)null,
        organismoResponsableId = 6, organismoResponsableNombre = "Obras",
        origenFinanciamientoId = (int?)null, origenFinanciamientoNombre = (string?)null,
        notas = new[]
        {
            new { id = 9, usuarioId = 1, fecha = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
                  texto = "Estado: Pendiente -> EnCurso", esAutomatica = true },
        },
    };

    private static DatosProyecto Datos() => new(
        "Plaza nueva", "con juegos", PrioridadTarea.Alta,
        new DateTime(2026, 12, 10, 15, 0, 0, DateTimeKind.Utc), 5000m, 4, null, 6, null);

    [Fact]
    public async Task CrearAsync_POSTProyectos_SerializaElBodyYDevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new ProyectoApiClient(TestHttp.CrearCliente(fake));

        var id = await client.CrearAsync(Datos());

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/proyectos", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"titulo\":\"Plaza nueva\"", fake.UltimoBody);
        Assert.Contains("\"presupuesto\":5000", fake.UltimoBody);
        Assert.Contains("\"zonaId\":4", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task CrearAsync_400_LanzaArgumentException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.BadRequest, "El título del proyecto es obligatorio."));
        var client = new ProyectoApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.CrearAsync(Datos() with { Titulo = "" }));
        Assert.Equal("El título del proyecto es obligatorio.", ex.Message);
    }

    [Fact]
    public async Task ModificarAsync_PUTProyecto_SerializaElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ProyectoApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(5, Datos());

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/proyectos/5", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"titulo\":\"Plaza nueva\"", fake.UltimoBody);
        Assert.Contains("2026-12-10T15:00:00Z", fake.UltimoBody);
    }

    [Fact]
    public async Task ModificarAsync_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(HttpStatusCode.NotFound, "Proyecto 5 no encontrado."));
        var client = new ProyectoApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.ModificarAsync(5, Datos()));
    }

    [Fact]
    public async Task CambiarEstadoAsync_POSTEstado_SerializaElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ProyectoApiClient(TestHttp.CrearCliente(fake));

        await client.CambiarEstadoAsync(5, EstadoProyecto.Terminado);

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/proyectos/5/estado", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"estado\":", fake.UltimoBody);
    }

    [Fact]
    public async Task CambiarEstadoAsync_409_LanzaReglaDeNegocio()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "No se puede pasar el proyecto de 'Pendiente' a 'Terminado'."));
        var client = new ProyectoApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.CambiarEstadoAsync(5, EstadoProyecto.Terminado));
    }

    [Fact]
    public async Task AgregarNotaAsync_POSTNotas_SerializaElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ProyectoApiClient(TestHttp.CrearCliente(fake));

        await client.AgregarNotaAsync(5, "avance registrado");

        Assert.Equal("/proyectos/5/notas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"texto\":\"avance registrado\"", fake.UltimoBody);
    }

    [Fact]
    public async Task AgregarNotaAsync_403_LanzaUnauthorizedAccess()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol autenticado no tiene permiso para esta acción."));
        var client = new ProyectoApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.AgregarNotaAsync(5, "x"));
    }

    [Fact]
    public async Task ListarAsync_GETProyectos_DeserializaLaListaConCatalogosYNotas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[] { ProyectoJson() }));
        var client = new ProyectoApiClient(TestHttp.CrearCliente(fake));

        var proyectos = await client.ListarAsync();

        Assert.Equal("/proyectos", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        var p = Assert.Single(proyectos);
        Assert.Equal(3, p.Id);
        Assert.Equal(EstadoProyecto.EnCurso, p.Estado);
        Assert.Equal(PrioridadTarea.Alta, p.Prioridad);
        Assert.Equal(5000m, p.Presupuesto);
        Assert.Equal(new DateTime(2026, 12, 10, 15, 0, 0, DateTimeKind.Utc), p.FechaLimite);
        Assert.Equal(DateTimeKind.Utc, p.FechaLimite!.Value.Kind);
        Assert.Equal("Centro", p.Zona!.Nombre);
        Assert.Equal("Obras", p.OrganismoResponsable!.Nombre);
        Assert.Null(p.DimensionTematica);
        Assert.Null(p.OrigenFinanciamiento);
        var nota = Assert.Single(p.Notas);
        Assert.True(nota.EsAutomatica);
        Assert.Equal(3, nota.ProyectoId);
    }

    [Fact]
    public async Task ObtenerFichaAsync_GETProyectoPorId_DeserializaGastos()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            proyecto = ProyectoJson(), totalGastado = 1200m, disponible = 3800m,
            gastos = new[]
            {
                new { id = 11, fecha = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                      proveedorNombre = "Ferretería Sur", detalle = "Cemento", montoTotal = 1200m },
            },
        }));
        var client = new ProyectoApiClient(TestHttp.CrearCliente(fake));

        var ficha = await client.ObtenerFichaAsync(3);

        Assert.Equal("/proyectos/3", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(3, ficha.Proyecto.Id);
        Assert.Equal(1200m, ficha.TotalGastado);
        Assert.Equal(3800m, ficha.Disponible);
        var gasto = Assert.Single(ficha.Gastos!);
        Assert.Equal("Ferretería Sur", gasto.ProveedorNombre);
        Assert.Equal(1200m, gasto.MontoTotal);
    }

    [Fact]
    public async Task ObtenerFichaAsync_GastosNull_QuedaNullYDisponibleNull()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            proyecto = ProyectoJson(), totalGastado = 0m, disponible = (decimal?)null,
            gastos = (object?)null,
        }));
        var client = new ProyectoApiClient(TestHttp.CrearCliente(fake));

        var ficha = await client.ObtenerFichaAsync(3);

        Assert.Null(ficha.Gastos);
        Assert.Null(ficha.Disponible);
    }

    [Fact]
    public async Task ObtenerFichaAsync_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(HttpStatusCode.NotFound, "Proyecto 3 no encontrado."));
        var client = new ProyectoApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.ObtenerFichaAsync(3));
    }

    [Fact]
    public async Task ListarOpcionesAsync_GETOpciones_DeserializaLasOpciones()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 3, titulo = "Plaza nueva", estado = EstadoProyecto.Pendiente },
            new { id = 4, titulo = "Veredas", estado = EstadoProyecto.EnCurso },
        }));
        var client = new ProyectoApiClient(TestHttp.CrearCliente(fake));

        var opciones = await client.ListarOpcionesAsync();

        Assert.Equal("/proyectos/opciones", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(2, opciones.Count);
        Assert.Equal(new OpcionProyectoDto(4, "Veredas", EstadoProyecto.EnCurso), opciones[1]);
    }
}
