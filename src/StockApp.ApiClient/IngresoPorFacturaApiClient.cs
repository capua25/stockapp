using System.Net.Http.Json;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient;

internal sealed record ProductoNuevoBody(
    string Codigo, string Nombre, int? CategoriaId, int UnidadMedidaId);

internal sealed record RenglonFacturaBody(
    int? ProductoId, ProductoNuevoBody? ProductoNuevo,
    decimal Cantidad, decimal PrecioUnitario, bool ActualizarPrecioCosto);

internal sealed record IngresoPorFacturaBody(
    int ProveedorId, string? NumeroFactura, string? NumeroOrden,
    DateTime Fecha, string Detalle, string? Destino, decimal MontoTotal,
    int FuenteFinanciamientoId, int RubroGastoId, int? LineaPoaId,
    CondicionPago CondicionPago, DateTime? FechaVencimiento,
    List<RenglonFacturaBody> Lineas);

/// <summary>IIngresoPorFacturaService contra /movimientos/ingreso-factura.</summary>
public sealed class IngresoPorFacturaApiClient : IIngresoPorFacturaService
{
    private readonly HttpClient _http;

    public IngresoPorFacturaApiClient(HttpClient http) => _http = http;

    public async Task<IngresoPorFacturaResultadoDto> RegistrarAsync(IngresoPorFacturaDto dto)
    {
        var body = new IngresoPorFacturaBody(
            dto.ProveedorId, dto.NumeroFactura, dto.NumeroOrden,
            dto.Fecha, dto.Detalle, dto.Destino, dto.MontoTotal,
            dto.FuenteFinanciamientoId, dto.RubroGastoId, dto.LineaPoaId,
            dto.CondicionPago, dto.FechaVencimiento,
            dto.Renglones.Select(r => new RenglonFacturaBody(
                r.ProductoId,
                r.ProductoNuevo is null ? null : new ProductoNuevoBody(
                    r.ProductoNuevo.Codigo, r.ProductoNuevo.Nombre, r.ProductoNuevo.CategoriaId,
                    r.ProductoNuevo.UnidadMedidaId),
                r.Cantidad, r.PrecioUnitario, r.ActualizarPrecioCosto)).ToList());

        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("movimientos/ingreso-factura", body));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<IngresoPorFacturaResultadoDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al registrar el ingreso por factura.");
    }

    public async Task AnularLoteAsync(int gastoId, bool confirmarAnulacionDePagoAutomatico = false)
    {
        var query = confirmarAnulacionDePagoAutomatico ? "?confirmar=true" : string.Empty;
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsync($"movimientos/ingreso-factura/{gastoId}/anular" + query, content: null));
        await ApiErrores.AsegurarExitoAsync(response);
    }
}
