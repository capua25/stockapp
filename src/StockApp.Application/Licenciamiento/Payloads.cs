using System.Text.Json.Serialization;

namespace StockApp.Application.Licenciamiento;

/// <summary>Payload de una licencia perpetua atada a una máquina (spec §11.4).</summary>
public record LicenciaPayload(
    [property: JsonPropertyName("ver")]     int    Ver,
    [property: JsonPropertyName("cliente")] string Cliente,
    [property: JsonPropertyName("maquina")] string Maquina,
    [property: JsonPropertyName("emitida")] string Emitida);

/// <summary>Payload de un token de reset de Admin, atado a máquina + desafío (spec §5.1).</summary>
public record TokenResetPayload(
    [property: JsonPropertyName("ver")]     int    Ver,
    [property: JsonPropertyName("accion")]  string Accion,
    [property: JsonPropertyName("maquina")] string Maquina,
    [property: JsonPropertyName("desafio")] string Desafio);
