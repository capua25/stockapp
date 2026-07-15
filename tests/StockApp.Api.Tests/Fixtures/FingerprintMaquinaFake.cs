using StockApp.Application.Licenciamiento;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>Fingerprint fijo para tests: nunca toca el registro / machine-id de la máquina real.</summary>
public sealed class FingerprintMaquinaFake : IFingerprintMaquina
{
    public string CodigoAgrupado => ClavesDePrueba.CodigoMaquina;
}
