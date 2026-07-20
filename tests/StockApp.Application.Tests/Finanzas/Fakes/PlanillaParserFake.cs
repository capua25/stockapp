using StockApp.Application.Finanzas;

namespace StockApp.Application.Tests.Finanzas.Fakes;

/// <summary>
/// Fake de <see cref="IPlanillaParser"/> (F5a): devuelve los DTOs fijos pasados por constructor
/// sin tocar el Stream. Permite testear <c>AnalisisImportacionService</c> sin un .ods real.
/// </summary>
public sealed class PlanillaParserFake : IPlanillaParser
{
    private readonly PlanillaGastosOds _gastos;
    private readonly PlanillaPoaOds _poa;

    public PlanillaParserFake(PlanillaGastosOds gastos, PlanillaPoaOds poa)
    {
        _gastos = gastos;
        _poa = poa;
    }

    public PlanillaGastosOds ParsearGastos(Stream odsStream) => _gastos;

    public PlanillaPoaOds ParsearPoa(Stream odsStream) => _poa;
}
