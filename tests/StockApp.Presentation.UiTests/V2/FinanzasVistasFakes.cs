using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
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

    public Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes) => throw new NotSupportedException();
    public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio) => throw new NotSupportedException();
}

internal sealed class GastoServiceSinUsoFake : IGastoService
{
    public Task<ResultadoGastoDto> AltaAsync(Gasto gasto, IReadOnlyList<int>? movimientoIds = null) => throw new NotSupportedException();
    public Task<ResultadoGastoDto> ModificarAsync(Gasto gasto) => throw new NotSupportedException();
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
