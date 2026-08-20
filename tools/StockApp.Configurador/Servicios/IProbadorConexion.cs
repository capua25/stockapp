using System.Threading;
using System.Threading.Tasks;

namespace StockApp.Configurador.Servicios;

/// <summary>Abstrae "Probar conexión" para poder testear ConfiguradorViewModel sin red real.</summary>
public interface IProbadorConexion
{
    Task<ResultadoPruebaConexion> ProbarAsync(string baseUrl, CancellationToken ct = default);
}
