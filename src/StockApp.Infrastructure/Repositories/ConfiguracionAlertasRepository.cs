using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class ConfiguracionAlertasRepository : IConfiguracionAlertasRepository
{
    private const int IdFilaUnica = 1;

    private readonly AppDbContext _ctx;

    public ConfiguracionAlertasRepository(AppDbContext ctx) => _ctx = ctx;

    public async Task<ConfiguracionAlertas> ObtenerAsync()
    {
        var fila = await _ctx.ConfiguracionesAlertas
            .FirstOrDefaultAsync(c => c.Id == IdFilaUnica);

        // Defensivo a propósito: en los tests, TRUNCATE "Usuarios" ... CASCADE arrastra esta
        // tabla y borra la fila sembrada por la migración. Devolver una instancia por defecto
        // en vez de null hace que todo el subsistema funcione igual, sembrado o no.
        return fila ?? new ConfiguracionAlertas { Id = IdFilaUnica, Habilitado = false };
    }

    public async Task GuardarAsync(ConfiguracionAlertas configuracion)
    {
        configuracion.Id = IdFilaUnica;

        var existente = await _ctx.ConfiguracionesAlertas
            .FirstOrDefaultAsync(c => c.Id == IdFilaUnica);

        if (existente is null)
        {
            _ctx.ConfiguracionesAlertas.Add(configuracion);
        }
        else
        {
            existente.UrlWebhook = configuracion.UrlWebhook;
            existente.Habilitado = configuracion.Habilitado;
            existente.ActualizadoEn = configuracion.ActualizadoEn;
            existente.ActualizadoPorUsuarioId = configuracion.ActualizadoPorUsuarioId;
        }

        await _ctx.SaveChangesAsync();
    }
}
