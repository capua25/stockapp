using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class TareaApiClientTests
{
    [Fact]
    public async Task CrearAsync_POSTTareas_SerializaElBody()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        var id = await client.CrearAsync(new Tarea { Titulo = "Reparar bache", Descripcion = "en calle Rivera" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/tareas", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"titulo\":\"Reparar bache\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task CrearAsync_400_LanzaArgumentException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.BadRequest, "El título de la tarea es obligatorio."));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => client.CrearAsync(new Tarea { Titulo = "" }));
        Assert.Equal("El título de la tarea es obligatorio.", ex.Message);
    }

    [Fact]
    public async Task ListarAsync_GETTareas_DeserializaLaListaConTomadaPor()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                id = 1, titulo = "Reparar bache", descripcion = (string?)null,
                estado = EstadoTarea.EnCurso, prioridad = PrioridadTarea.Media, fechaLimite = (DateTime?)null,
                creadaPorUsuarioId = 1, fechaCreacion = new DateTime(2026, 8, 1),
                tomadaPorUsuarioId = 2, tomadaPorNombre = "juan", fechaInicio = new DateTime(2026, 8, 1),
                cerradaPorUsuarioId = (int?)null, fechaFin = (DateTime?)null,
                notas = Array.Empty<object>(),
            },
        }));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        var tareas = await client.ListarAsync();

        var tarea = Assert.Single(tareas);
        Assert.Equal(EstadoTarea.EnCurso, tarea.Estado);
        Assert.Equal("juan", tarea.TomadaPor!.NombreUsuario);
    }

    [Fact]
    public async Task TomarAsync_POSTTomar_ConLaRutaCorrecta()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await client.TomarAsync(5);

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/tareas/5/tomar", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task TomarAsync_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(HttpStatusCode.NotFound, "Tarea 5 no encontrada."));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.TomarAsync(5));
    }

    [Fact]
    public async Task TerminarAsync_409_LanzaReglaDeNegocio()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "No se puede pasar la tarea de 'Pendiente' a 'Terminada'."));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.TerminarAsync(5));
    }

    [Fact]
    public async Task CancelarAsync_403_LanzaUnauthorizedAccess()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol autenticado no tiene permiso para esta acción."));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.CancelarAsync(5));
    }

    [Fact]
    public async Task CambiarPrioridadAsync_POSTPrioridad_SerializaElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await client.CambiarPrioridadAsync(5, PrioridadTarea.Alta);

        Assert.Equal("/tareas/5/prioridad", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"prioridad\":", fake.UltimoBody);
    }

    [Fact]
    public async Task AgregarNotaAsync_POSTNotas_SerializaElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await client.AgregarNotaAsync(5, "avance registrado");

        Assert.Equal("/tareas/5/notas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"texto\":\"avance registrado\"", fake.UltimoBody);
    }

    [Fact]
    public async Task ObtenerPorIdAsync_200_DeserializaLaTarea()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            id = 5, titulo = "Reparar bache", descripcion = (string?)null,
            estado = EstadoTarea.Pendiente, prioridad = PrioridadTarea.Media, fechaLimite = (DateTime?)null,
            creadaPorUsuarioId = 1, fechaCreacion = new DateTime(2026, 9, 1),
            tomadaPorUsuarioId = (int?)null, tomadaPorNombre = (string?)null, fechaInicio = (DateTime?)null,
            cerradaPorUsuarioId = (int?)null, fechaFin = (DateTime?)null,
            zonaId = (int?)null, zonaNombre = (string?)null,
            dimensionTematicaId = (int?)null, dimensionTematicaNombre = (string?)null,
            organismoResponsableId = (int?)null, organismoResponsableNombre = (string?)null,
            origenFinanciamientoId = (int?)null, origenFinanciamientoNombre = (string?)null,
            documentoAdministrativoId = (int?)null, documentoAdministrativoNumero = (string?)null,
            notas = Array.Empty<object>(),
        }));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        var tarea = await client.ObtenerPorIdAsync(5);

        Assert.Equal("/tareas/5", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("Reparar bache", tarea!.Titulo);
    }

    [Fact]
    public async Task ObtenerPorIdAsync_404_DevuelveNull()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(HttpStatusCode.NotFound, "Tarea 999 no encontrada."));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        var tarea = await client.ObtenerPorIdAsync(999);

        Assert.Null(tarea);
    }

    [Fact]
    public async Task CrearAsync_ConClasificacion_SerializaLosCincoIds()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 1 }, HttpStatusCode.Created));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await client.CrearAsync(new Tarea
        {
            Titulo = "x", ZonaId = 3, DimensionTematicaId = 4,
            OrganismoResponsableId = 5, OrigenFinanciamientoId = 6, DocumentoAdministrativoId = 7,
        });

        Assert.Contains("\"zonaId\":3", fake.UltimoBody);
        Assert.Contains("\"dimensionTematicaId\":4", fake.UltimoBody);
        Assert.Contains("\"organismoResponsableId\":5", fake.UltimoBody);
        Assert.Contains("\"origenFinanciamientoId\":6", fake.UltimoBody);
        Assert.Contains("\"documentoAdministrativoId\":7", fake.UltimoBody);
    }

    [Fact]
    public async Task ListarAsync_ConClasificacion_DeserializaLosNombres()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                id = 1, titulo = "x", descripcion = (string?)null,
                estado = EstadoTarea.Pendiente, prioridad = PrioridadTarea.Media, fechaLimite = (DateTime?)null,
                creadaPorUsuarioId = 1, fechaCreacion = new DateTime(2026, 9, 1),
                tomadaPorUsuarioId = (int?)null, tomadaPorNombre = (string?)null, fechaInicio = (DateTime?)null,
                cerradaPorUsuarioId = (int?)null, fechaFin = (DateTime?)null,
                zonaId = 3, zonaNombre = "Centro",
                dimensionTematicaId = (int?)null, dimensionTematicaNombre = (string?)null,
                organismoResponsableId = (int?)null, organismoResponsableNombre = (string?)null,
                origenFinanciamientoId = (int?)null, origenFinanciamientoNombre = (string?)null,
                documentoAdministrativoId = (int?)null, documentoAdministrativoNumero = (string?)null,
                notas = Array.Empty<object>(),
            },
        }));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        var tareas = await client.ListarAsync();

        var tarea = Assert.Single(tareas);
        Assert.Equal(3, tarea.ZonaId);
        Assert.Equal("Centro", tarea.Zona!.Nombre);
    }

    [Fact]
    public async Task ReclasificarAsync_PUTClasificacion_SerializaElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await client.ReclasificarAsync(5, new DatosClasificacionTarea(1, null, null, null, null));

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/tareas/5/clasificacion", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"zonaId\":1", fake.UltimoBody);
        Assert.Contains("\"dimensionTematicaId\":null", fake.UltimoBody);
    }

    [Fact]
    public async Task ReclasificarAsync_403_LanzaUnauthorizedAccessException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(HttpStatusCode.Forbidden, "sin permiso"));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => client.ReclasificarAsync(5, new DatosClasificacionTarea(null, null, null, null, null)));
    }

    [Fact]
    public async Task ListarPorDocumentoAsync_GETConQueryString_DeserializaLaLista()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        var tareas = await client.ListarPorDocumentoAsync(9);

        Assert.Equal("/tareas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("documentoId=9", fake.UltimaRequest.RequestUri!.Query);
        Assert.Empty(tareas);
    }
}
