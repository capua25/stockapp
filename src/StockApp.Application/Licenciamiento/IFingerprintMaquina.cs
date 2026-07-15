namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Huella estable de la máquina donde corre la API, presentada como código agrupado
/// (ej. A3F2-9B41-...). Nunca expone el id crudo del OS.
/// </summary>
public interface IFingerprintMaquina
{
    string CodigoAgrupado { get; }
}
