using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class DocumentosEndpointTests : ApiTestBase
{
    public DocumentosEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private HttpClient ClienteAutenticado(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task SeedUsuariosAsync()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
    }

    private async Task<int> CrearDocumentoAsync(
        HttpClient client, string numero = "0087", int anio = 2026, TipoDocumento tipo = TipoDocumento.Expediente)
    {
        var response = await client.PostAsJsonAsync("/documentos",
            new CrearDocumentoRequest(numero, anio, tipo, new DateTime(2026, 1, 15), "Descripción de prueba"));
        var creado = await response.Content.ReadFromJsonAsync<DocumentoCreadoResponse>();
        return creado!.Id;
    }

    /// <summary>Siembra un documento directo por EF (sin pasar por los endpoints de transición,
    /// que todavía no existen en esta tarea) — mismo criterio que
    /// AdjuntosEndpointTests.SembrarGastoAsync. El registrante es un Admin propio con nombre
    /// único (Guid) para no colisionar con "admin.test"/"operador.test" de SeedUsuariosAsync.</summary>
    private async Task<int> SembrarDocumentoAsync(
        EstadoDocumento estado = EstadoDocumento.Pendiente,
        string numero = "0001", int anio = 2026, TipoDocumento tipo = TipoDocumento.Expediente)
    {
        await using var ctx = Factory.CrearContexto();
        var registrante = await DatosDePrueba.SeedUsuarioAsync(
            ctx, $"registrante.{Guid.NewGuid():N}", "Secreta123!", RolUsuario.Admin);

        var documento = new DocumentoAdministrativo
        {
            Numero = numero,
            Anio = anio,
            Tipo = tipo,
            FechaEmision = DateTime.SpecifyKind(new DateTime(2026, 1, 15), DateTimeKind.Utc),
            Descripcion = "Documento de prueba",
            Estado = estado,
            RegistradoPorUsuarioId = registrante.Id,
            FechaRegistro = DateTime.UtcNow,
            FechaCierre = estado is EstadoDocumento.Finalizado or EstadoDocumento.Anulado
                ? DateTime.UtcNow : null,
        };
        ctx.DocumentosAdministrativos.Add(documento);
        await ctx.SaveChangesAsync();
        return documento.Id;
    }

    [Fact]
    public async Task PostDocumentos_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().PostAsJsonAsync("/documentos",
            new CrearDocumentoRequest("0087", 2026, TipoDocumento.Expediente, new DateTime(2026, 1, 15), "x"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostDocumentos_ConTokenOperadorSinPermiso_Devuelve403()
    {
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.PostAsJsonAsync("/documentos",
            new CrearDocumentoRequest("0087", 2026, TipoDocumento.Expediente, new DateTime(2026, 1, 15), "x"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostDocumentos_ConTokenOperador_Devuelve201()
    {
        // D7: documentos.gestionar es configurable y se agrega a PermisosInicialesOperador
        // (Bloques A/B) — un Operador recién sembrado con SeedUsuarioAsync ya lo trae.
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync("/documentos",
            new CrearDocumentoRequest("0087", 2026, TipoDocumento.Expediente, new DateTime(2026, 1, 15), "Expediente de prueba"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var creado = await response.Content.ReadFromJsonAsync<DocumentoCreadoResponse>();
        Assert.True(creado!.Id > 0);
    }

    [Fact]
    public async Task PostDocumentos_NumeroDuplicado_Devuelve409()
    {
        // D1: índice único (Tipo, Anio, Numero) — condición de carrera real, contra Postgres real.
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        await CrearDocumentoAsync(client, numero: "0087", anio: 2026, tipo: TipoDocumento.Expediente);

        var response = await client.PostAsJsonAsync("/documentos",
            new CrearDocumentoRequest("0087", 2026, TipoDocumento.Expediente, new DateTime(2026, 2, 1), "Otro expediente"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetActivos_ConTokenOperador_Devuelve200SoloConPendientesYEnProceso()
    {
        // Orden: SeedUsuariosAsync PRIMERO — TokenOperador() usa un Id hardcodeado (2) que debe
        // coincidir con "operador.test"; sembrar documentos antes correría los Ids y el Id=2
        // pasaría a ser un "registrante.{guid}" (Admin) sin filas en PermisosUsuario, dando 403
        // por el fail-closed de PoblarPermisosMiddleware (ver doc de SeedOperadorConTokenAsync).
        await SeedUsuariosAsync();
        await SembrarDocumentoAsync(EstadoDocumento.Pendiente, numero: "0001");
        await SembrarDocumentoAsync(EstadoDocumento.EnProceso, numero: "0002");
        await SembrarDocumentoAsync(EstadoDocumento.Finalizado, numero: "0003");
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.GetAsync("/documentos/activos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var documentos = await response.Content.ReadFromJsonAsync<List<DocumentoDto>>();
        Assert.Equal(2, documentos!.Count);
    }

    [Fact]
    public async Task GetActivos_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: un test de 403 por endpoint, no representativo — GET /activos tiene su propia
        // policy y puede quedar mal cableada aunque POST /documentos esté bien.
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.GetAsync("/documentos/activos");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetHistorial_SinAnio_Devuelve400()
    {
        // D9: Anio es obligatorio en el historial — ArgumentException, no ReglaDeNegocioException,
        // porque es un request mal formado, no un conflicto de negocio (contraste con el 409 de arriba).
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.GetAsync("/documentos/historial");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetHistorial_ConAnio_Devuelve200SoloConCerrados()
    {
        // Orden: SeedUsuariosAsync PRIMERO — ver comentario en GetActivos_.../200SoloConPendientesYEnProceso.
        await SeedUsuariosAsync();
        await SembrarDocumentoAsync(EstadoDocumento.Finalizado, numero: "0010", anio: 2026);
        await SembrarDocumentoAsync(EstadoDocumento.Pendiente, numero: "0011", anio: 2026);
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.GetAsync("/documentos/historial?anio=2026");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var documentos = await response.Content.ReadFromJsonAsync<List<DocumentoDto>>();
        var documento = Assert.Single(documentos!);
        Assert.Equal("0010", documento.Numero);
    }

    [Fact]
    public async Task GetHistorial_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: 403 propio, no representativo del de POST /documentos.
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.GetAsync("/documentos/historial?anio=2026");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPorId_Inexistente_Devuelve404()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.GetAsync("/documentos/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPorId_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: 403 propio, no representativo del de POST /documentos.
        await using var ctx = Factory.CrearContexto();
        var admin = await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var clienteAdmin = ClienteAutenticado(jwt.GenerarToken(admin.Id, RolUsuario.Admin));
        var id = await CrearDocumentoAsync(clienteAdmin);
        var clienteSinPermiso = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await clienteSinPermiso.GetAsync($"/documentos/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPorId_Existente_Devuelve200ConLosDatos()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var id = await CrearDocumentoAsync(client);

        var response = await client.GetAsync($"/documentos/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var documento = await response.Content.ReadFromJsonAsync<DocumentoDto>();
        Assert.Equal("0087", documento!.Numero);
        Assert.Equal(EstadoDocumento.Pendiente, documento.Estado);
    }

    [Fact]
    public async Task GetPorId_Existente_TraeRegistradoPorNombre()
    {
        // F3: ObtenerPorIdAsync devolvía RegistradoPor null porque el repositorio no lo incluía
        // (Task 5) -- este test pega contra la Api real para que el bug no vuelva en la próxima
        // refactorización del repositorio.
        await using var ctx = Factory.CrearContexto();
        var registrante = await DatosDePrueba.SeedUsuarioAsync(
            ctx, "registrante.conocido", "Secreta123!", RolUsuario.Admin);
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var clienteRegistrante = ClienteAutenticado(jwt.GenerarToken(registrante.Id, RolUsuario.Admin));
        var id = await CrearDocumentoAsync(clienteRegistrante);

        var response = await clienteRegistrante.GetAsync($"/documentos/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var documento = await response.Content.ReadFromJsonAsync<DocumentoDto>();
        Assert.Equal("registrante.conocido", documento!.RegistradoPorNombre);
    }

    [Fact]
    public async Task PutDocumentos_CorrigeNumero_Devuelve200()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var id = await CrearDocumentoAsync(client, numero: "0087");

        var response = await client.PutAsJsonAsync($"/documentos/{id}",
            new EditarDocumentoRequest("0088", 2026, TipoDocumento.Expediente, new DateTime(2026, 1, 15), "Descripción de prueba"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PutDocumentos_NumeroChocaConOtroDocumento_Devuelve409()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        await CrearDocumentoAsync(client, numero: "0087");
        var idSegundo = await CrearDocumentoAsync(client, numero: "0088");

        var response = await client.PutAsJsonAsync($"/documentos/{idSegundo}",
            new EditarDocumentoRequest("0087", 2026, TipoDocumento.Expediente, new DateTime(2026, 1, 15), "Descripción de prueba"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutDocumentos_ConTokenOperadorSinPermiso_Devuelve403()
    {
        await using var ctx = Factory.CrearContexto();
        var admin = await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var clienteAdmin = ClienteAutenticado(jwt.GenerarToken(admin.Id, RolUsuario.Admin));
        var id = await CrearDocumentoAsync(clienteAdmin);
        var clienteSinPermiso = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await clienteSinPermiso.PutAsJsonAsync($"/documentos/{id}",
            new EditarDocumentoRequest("0099", 2026, TipoDocumento.Expediente, new DateTime(2026, 1, 15), "x"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostIniciar_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().PostAsync("/documentos/1/iniciar", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostIniciar_DocumentoInexistente_Devuelve404()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync("/documentos/9999/iniciar", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostIniciar_ConTokenOperador_Devuelve200()
    {
        // Orden: SeedUsuariosAsync PRIMERO — ver comentario en GetActivos_.../200SoloConPendientesYEnProceso.
        await SeedUsuariosAsync();
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync($"/documentos/{id}/iniciar", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostIniciar_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: 403 propio de /iniciar, no representativo del de otro endpoint.
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.PostAsync($"/documentos/{id}/iniciar", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostVolverAPendiente_DesdeEnProceso_Devuelve200()
    {
        // Análogo de TareaService.SoltarAsync (D4). Orden: SeedUsuariosAsync PRIMERO — ver
        // comentario en GetActivos_.../200SoloConPendientesYEnProceso.
        await SeedUsuariosAsync();
        var id = await SembrarDocumentoAsync(EstadoDocumento.EnProceso);
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync($"/documentos/{id}/volver-a-pendiente", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostVolverAPendiente_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: 403 propio de /volver-a-pendiente.
        var id = await SembrarDocumentoAsync(EstadoDocumento.EnProceso);
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.PostAsync($"/documentos/{id}/volver-a-pendiente", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostFinalizar_DesdePendiente_Devuelve409()
    {
        // Máquina de estados (D4): Pendiente no puede pasar directo a Finalizado.
        // Orden: SeedUsuariosAsync PRIMERO — ver comentario en GetActivos_.../200SoloConPendientesYEnProceso.
        await SeedUsuariosAsync();
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync($"/documentos/{id}/finalizar", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostFinalizar_DesdeEnProceso_Devuelve200()
    {
        // Orden: SeedUsuariosAsync PRIMERO — ver comentario en GetActivos_.../200SoloConPendientesYEnProceso.
        await SeedUsuariosAsync();
        var id = await SembrarDocumentoAsync(EstadoDocumento.EnProceso);
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync($"/documentos/{id}/finalizar", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostFinalizar_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: 403 propio de /finalizar.
        var id = await SembrarDocumentoAsync(EstadoDocumento.EnProceso);
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.PostAsync($"/documentos/{id}/finalizar", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostNotas_ConTokenOperador_Devuelve200()
    {
        // Orden: SeedUsuariosAsync PRIMERO — ver comentario en GetActivos_.../200SoloConPendientesYEnProceso.
        await SeedUsuariosAsync();
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/notas", new AgregarNotaDocumentoRequest("avance del trámite"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostNotas_ConTokenOperadorSinPermiso_Devuelve403()
    {
        // C7: 403 propio de /notas.
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "operador.sinpermiso", "Secreta123!", Array.Empty<string>());
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var client = ClienteAutenticado(jwt.GenerarToken(operador.Id, RolUsuario.Operador));

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/notas", new AgregarNotaDocumentoRequest("avance del trámite"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostAnular_ConTokenOperador_Devuelve403()
    {
        // D7: documentos.administrar es estructural, Operador nunca lo tiene.
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/anular", new MotivoRequest("el interesado desistió"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostAnular_ConTokenAdmin_MotivoVacio_Devuelve409()
    {
        // D8: motivo obligatorio -> ReglaDeNegocioException, no una validación de request (400).
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync($"/documentos/{id}/anular", new MotivoRequest(""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostAnular_ConTokenAdmin_MotivoNulo_Devuelve409()
    {
        // Pedido explícito: por HTTP el JSON puede traer "motivo": null aunque la firma de C#
        // (string motivo) no admita null en compilación -- STJ no lo rechaza sin
        // RespectNullableAnnotations, así que debe llegar a IsNullOrWhiteSpace -> 409, no 500/400.
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/anular", new { motivo = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostAnular_ConTokenAdmin_Devuelve200()
    {
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/anular", new MotivoRequest("el interesado desistió"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostReabrir_ConTokenOperador_Devuelve403()
    {
        // D7: documentos.administrar es estructural, Operador nunca lo tiene -- 403 propio de
        // /reabrir, no representativo del de /anular (C7).
        var id = await SembrarDocumentoAsync(EstadoDocumento.Finalizado);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/reabrir", new MotivoRequest("se encontró documentación nueva"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostReabrir_SobreDocumentoNoCerrado_Devuelve409()
    {
        // D8: Pendiente -> EnProceso ya es válida por otra vía (iniciar); ReabrirAsync exige
        // EsCerrado explícitamente, así que sobre Pendiente debe rechazar, no dejarlo pasar
        // como si fuera un IniciarProcesoAsync más.
        var id = await SembrarDocumentoAsync(EstadoDocumento.Pendiente);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/reabrir", new MotivoRequest("se encontró documentación nueva"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostReabrir_ConTokenAdmin_MotivoVacio_Devuelve409()
    {
        // D8: motivo obligatorio -> ReglaDeNegocioException, no una validación de request (400).
        var id = await SembrarDocumentoAsync(EstadoDocumento.Finalizado);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync($"/documentos/{id}/reabrir", new MotivoRequest(""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostReabrir_ConTokenAdmin_MotivoNulo_Devuelve409()
    {
        // Pedido explícito: mismo caso que PostAnular_ConTokenAdmin_MotivoNulo_Devuelve409,
        // pero del lado de /reabrir -- sobre un documento cerrado para no confundir con el 409
        // de "no cerrado" del test anterior.
        var id = await SembrarDocumentoAsync(EstadoDocumento.Finalizado);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/reabrir", new { motivo = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostReabrir_ConTokenAdmin_SobreDocumentoCerrado_Devuelve200()
    {
        var id = await SembrarDocumentoAsync(EstadoDocumento.Finalizado);
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync(
            $"/documentos/{id}/reabrir", new MotivoRequest("se encontró documentación nueva"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
