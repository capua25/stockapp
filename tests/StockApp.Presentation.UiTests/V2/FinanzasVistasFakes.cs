using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Domain.Entities;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>IFinanzasVistasService con los tipos reales. Cada consulta responde, en este orden: Falla, Pendiente o el valor.</summary>
internal sealed class FinanzasVistasFake : IFinanzasVistasService
{
    public CalendarioPagosDto Calendario { get; set; } = new([], [], [], []);
    public TaskCompletionSource<CalendarioPagosDto>? PendienteCalendario { get; set; }
    public Exception? FallaCalendario { get; set; }

    public List<ControlPoaLineaDto> Lineas { get; set; } = new();
    public TaskCompletionSource<IReadOnlyList<ControlPoaLineaDto>>? PendientePoa { get; set; }
    public Exception? FallaPoa { get; set; }
    public List<int> EjerciciosConsultados { get; } = new();

    public Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null) =>
        FallaCalendario is not null ? Task.FromException<CalendarioPagosDto>(FallaCalendario)
        : PendienteCalendario is not null ? PendienteCalendario.Task
        : Task.FromResult(Calendario);

    public Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio)
    {
        EjerciciosConsultados.Add(ejercicio);
        return FallaPoa is not null ? Task.FromException<IReadOnlyList<ControlPoaLineaDto>>(FallaPoa)
            : PendientePoa is not null ? PendientePoa.Task
            : Task.FromResult<IReadOnlyList<ControlPoaLineaDto>>(Lineas.ToArray());
    }

    public Dictionary<(int Anio, int Mes), LibroCajaMesDto> Meses { get; } = new();
    public Dictionary<int, LibroCajaAnualDto> Anios { get; } = new();
    public TaskCompletionSource<LibroCajaMesDto>? PendienteMes { get; set; }
    public Exception? FallaLibroCaja { get; set; }

    /// <summary>Consultas de libro caja en orden: "2026-10" para un mes, "2026" para un año.</summary>
    public List<string> ConsultasLibroCaja { get; } = new();

    /// <summary>Sin datos cargados para ese mes responde un mes vacío, como el servidor.</summary>
    public Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(Caja caja, int anio, int mes)
    {
        ConsultasLibroCaja.Add($"{anio}-{mes:00}");
        return FallaLibroCaja is not null ? Task.FromException<LibroCajaMesDto>(FallaLibroCaja)
            : PendienteMes is not null ? PendienteMes.Task
            : Task.FromResult(Meses.TryGetValue((anio, mes), out var libro) ? libro : new LibroCajaMesDto(anio, mes, 0m, 0m, [], [], []));
    }

    /// <summary>Sin datos cargados para ese año responde los 12 meses en cero, como el servidor.</summary>
    public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(Caja caja, int anio)
    {
        ConsultasLibroCaja.Add($"{anio}");
        return FallaLibroCaja is not null ? Task.FromException<LibroCajaAnualDto>(FallaLibroCaja)
            : Task.FromResult(Anios.TryGetValue(anio, out var libro) ? libro
                : new LibroCajaAnualDto(anio, Enumerable.Range(1, 12).Select(m => new TotalMensualDto(m, 0m, 0m, 0m)).ToList(), []));
    }
}

internal sealed class GastoServiceSinUsoFake : IGastoService
{
    public Task<ResultadoGastoDto> AltaAsync(Gasto gasto, PagoContadoDto? pagoContado, IReadOnlyList<int>? movimientoIds = null) => throw new NotSupportedException();
    public Task<ResultadoGastoDto> ModificarAsync(Gasto gasto, PagoContadoDto? pagoContado) => throw new NotSupportedException();
    public Task AnularAsync(int id, bool confirmarAnulacionDePagoAutomatico = false) => throw new NotSupportedException();
    public Task<Gasto> ObtenerPorIdAsync(int id) => throw new NotSupportedException();
    public Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura, string? numeroOrden) => throw new NotSupportedException();
    public Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro) => throw new NotSupportedException();
    public Task<int> RegistrarPagoAsync(PagoGasto pago) => throw new NotSupportedException();
    public Task AnularPagoAsync(int gastoId, int pagoId) => throw new NotSupportedException();
    public Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds) => throw new NotSupportedException();
}

/// <summary>Guardado que la vista no ejercita: si algo los llama, el test se entera.</summary>
internal sealed class GuardadoSinUsoFake : IServicioGuardadoArchivo
{
    public Task<bool> GuardarBytesAsync(System.IO.Stream contenido, string nombreSugerido, System.Threading.CancellationToken ct = default,
        string? extension = null, string? tipoMime = null) => throw new NotSupportedException();
}

internal sealed class PdfExporterSinUsoFake : IPdfExporter
{
    public byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<ColumnaPdf> columnas, MetadatosDocumento metadatos, ResumenPdf? resumen = null)
        => throw new NotSupportedException();
}
