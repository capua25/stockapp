using StockApp.Application.Licenciamiento;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>Almacén de licencia en memoria para tests (sin tocar el filesystem del server).</summary>
public sealed class AlmacenLicenciaEnMemoria : IAlmacenLicencia
{
    private string? _licencia;
    public AlmacenLicenciaEnMemoria(string? inicial = null) => _licencia = inicial;
    public Task<string?> LeerAsync() => Task.FromResult(_licencia);
    public Task GuardarAsync(string licencia) { _licencia = licencia; return Task.CompletedTask; }
}
