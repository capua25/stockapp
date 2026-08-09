using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Interfaces;
using StockApp.Infrastructure.Notificaciones;
using Xunit;

namespace StockApp.Api.Tests.Backups;

/// <summary>
/// Guardián de composición DI (fix crítico, ronda de review 1/5 de la Task 3): sin este
/// test, si el registro de <see cref="INotificadorAlertas"/> en Program.cs se degradara a
/// un no-op (ej. volviera a NotificadorAlertasNulo, o el AddHttpClient se borrara sin
/// querer), el build compila, ValidateOnBuild pasa y toda la suite de StockApp.Api.Tests
/// sigue en verde -- pero en producción ningún backup fallido ni ningún silencio
/// prolongado notificaría nada, nunca, sin ningún síntoma visible. Este es exactamente el
/// modo de falla que toda la feature existe para eliminar; este test es lo único que lo
/// detecta.
/// </summary>
public class CanalAlertaBackupComposicionTests : ApiTestBase
{
    public CanalAlertaBackupComposicionTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public void INotificadorAlertas_SeResuelveComoNotificadorWebhook_EnElHostRealDeProduccion()
    {
        // Dentro de un scope, no del root provider: NotificadorWebhook se registra vía
        // AddHttpClient<,> (Transient) y depende de IConfiguracionAlertasRepository
        // (Scoped, usa AppDbContext) -- resolverlo desde el root tira una excepción de
        // validación de scopes. ApiFactory no reemplaza INotificadorAlertas en
        // ConfigureTestServices, así que esto ejercita el registro real de Program.cs.
        using var scope = Factory.Services.CreateScope();

        var notificador = scope.ServiceProvider.GetRequiredService<INotificadorAlertas>();

        Assert.IsType<NotificadorWebhook>(notificador);
    }
}
