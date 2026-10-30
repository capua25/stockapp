using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>IReporteStockService con los tipos reales. Cada consulta responde, en este orden: Falla, Pendiente o la lista, y graba
/// cuántas veces la llamaron (y con qué argumentos las que tienen filtros). Las fechas llegan en UTC, como del VM.</summary>
internal sealed class ReporteStockFake : IReporteStockService
{
    public List<ValorizacionItemDto> Valorizacion { get; set; } = new();
    public TaskCompletionSource<ValorizacionReporteDto>? PendienteValorizacion { get; set; }
    public Exception? FallaValorizacion { get; set; }
    public int LlamadasValorizacion { get; private set; }

    public List<StockCategoriaDto> Categorias { get; set; } = new();
    public Exception? FallaCategorias { get; set; }
    public int LlamadasCategorias { get; private set; }

    public List<MasMovidoDto> MasMovidos { get; set; } = new();
    public Exception? FallaMasMovidos { get; set; }
    public List<(DateTime? Desde, DateTime? Hasta, int Top)> ConsultasMasMovidos { get; } = new();

    public List<MovimientoHistorialDto> Historial { get; set; } = new();
    public Exception? FallaHistorial { get; set; }
    public List<(int ProductoId, DateTime? Desde, DateTime? Hasta)> ConsultasHistorial { get; } = new();

    public Task<ValorizacionReporteDto> ObtenerValorizacionAsync()
    {
        LlamadasValorizacion++;
        return FallaValorizacion is not null ? Task.FromException<ValorizacionReporteDto>(FallaValorizacion)
            : PendienteValorizacion is not null ? PendienteValorizacion.Task
            : Task.FromResult(new ValorizacionReporteDto(Valorizacion.ToList(), new ValorizacionTotalesDto(Valorizacion.Sum(i => i.ValorCosto))));
    }

    public Task<IReadOnlyList<StockCategoriaDto>> ObtenerStockPorCategoriaAsync()
    {
        LlamadasCategorias++;
        return FallaCategorias is not null ? Task.FromException<IReadOnlyList<StockCategoriaDto>>(FallaCategorias)
            : Task.FromResult<IReadOnlyList<StockCategoriaDto>>(Categorias.ToList());
    }

    public Task<IReadOnlyList<MasMovidoDto>> ObtenerMasMovidosAsync(DateTime? fechaDesde, DateTime? fechaHasta, int topN = 20)
    {
        ConsultasMasMovidos.Add((fechaDesde, fechaHasta, topN));
        return FallaMasMovidos is not null ? Task.FromException<IReadOnlyList<MasMovidoDto>>(FallaMasMovidos)
            : Task.FromResult<IReadOnlyList<MasMovidoDto>>(MasMovidos.ToList());
    }

    public Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialPorProductoAsync(int productoId, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        ConsultasHistorial.Add((productoId, fechaDesde, fechaHasta));
        return FallaHistorial is not null ? Task.FromException<IReadOnlyList<MovimientoHistorialDto>>(FallaHistorial)
            : Task.FromResult<IReadOnlyList<MovimientoHistorialDto>>(Historial.ToList());
    }
}

internal sealed class ReporteProyectosFake : IReporteProyectosService
{
    public List<FilaReporteProyectos> Filas { get; set; } = new();
    public Exception? Falla { get; set; }
    public List<FiltroReporteProyectos> Consultas { get; } = new();

    public Task<ReporteProyectosDto> ObtenerAsync(FiltroReporteProyectos filtro)
    {
        Consultas.Add(filtro);
        return Falla is not null ? Task.FromException<ReporteProyectosDto>(Falla)
            : Task.FromResult(new ReporteProyectosDto(Filas.ToList(), new FilaReporteProyectos("Total general", Filas.Sum(f => f.Pendientes), Filas.Sum(f => f.EnCurso), Filas.Sum(f => f.Terminados), Filas.Sum(f => f.Cancelados), Filas.Sum(f => f.Total))));
    }
}

internal sealed class AuditoriaQueryFake : IAuditoriaQueryService
{
    public List<AuditoriaItemDto> Registros { get; set; } = new();
    public Exception? Falla { get; set; }
    public List<(int? UsuarioId, DateTime? Desde, DateTime? Hasta)> Consultas { get; } = new();

    public Task<IReadOnlyList<AuditoriaItemDto>> ObtenerLogAsync(int? usuarioId, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        Consultas.Add((usuarioId, fechaDesde, fechaHasta));
        return Falla is not null ? Task.FromException<IReadOnlyList<AuditoriaItemDto>>(Falla)
            : Task.FromResult<IReadOnlyList<AuditoriaItemDto>>(Registros.ToList());
    }
}

/// <summary>IUsuarioService solo con ListarAsync (lo único que usa el Log de auditoría).</summary>
internal sealed class UsuariosListaFake : IUsuarioService
{
    public List<UsuarioDto> Lista { get; set; } = new();
    public Exception? Falla { get; set; }
    public int Llamadas { get; private set; }

    /// <summary>Sesión que "llama": con ella el fake respeta el contrato real de <c>GET /usuarios</c>, que exige <c>usuarios.gestionar</c>
    /// (estructural de Admin) y da 403 a cualquier Operador.</summary>
    public StockApp.Application.Interfaces.ICurrentSession? Sesion { get; set; }

    public Task<IReadOnlyList<UsuarioDto>> ListarAsync()
    {
        Llamadas++;
        if (Sesion is { } sesion && sesion.RolActual != RolUsuario.Admin
            && !sesion.PermisosActuales.Contains(StockApp.Application.Authorization.Permisos.GestionarUsuarios))
            return Task.FromException<IReadOnlyList<UsuarioDto>>(new UnauthorizedAccessException());
        return Falla is not null ? Task.FromException<IReadOnlyList<UsuarioDto>>(Falla)
            : Task.FromResult<IReadOnlyList<UsuarioDto>>(Lista.ToList());
    }

    public Task<int> AltaUsuarioAsync(string nombreUsuario, string? nombreCompleto, string contrasenaPlan, RolUsuario rol) => throw new NotSupportedException();
    public Task BajaLogicaAsync(int usuarioId) => throw new NotSupportedException();
    public Task CambiarRolAsync(int usuarioId, RolUsuario nuevoRol) => throw new NotSupportedException();
    public Task CambiarContrasenaAsync(int usuarioId, string nuevaContrasenaPlan, string? contrasenaActualPlan = null) => throw new NotSupportedException();
    public Task<IReadOnlyList<string>> ObtenerPermisosAsync(int usuarioId) => throw new NotSupportedException();
    public Task GuardarPermisosAsync(int usuarioId, IReadOnlyList<string> permisos) => throw new NotSupportedException();
}

/// <summary>IProductoService solo con BuscarPorTextoAsync (el buscador de Historial por producto).</summary>
internal sealed class ProductosBusquedaFake : IProductoService
{
    public List<ProductoDto> Resultados { get; set; } = new();

    public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto) => Task.FromResult<IReadOnlyList<ProductoDto>>(Resultados.ToList());
    public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre) => Task.FromResult<IReadOnlyList<ProductoDto>>(Resultados.ToList());
    public Task<int> AltaAsync(Producto producto) => throw new NotSupportedException();
    public Task ModificarAsync(Producto producto) => throw new NotSupportedException();
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
    public Task CambiarPrecioAsync(int id, decimal precioCosto) => throw new NotSupportedException();
}

/// <summary>Datos de prueba de los reportes (dominio chico: lo justo para cada caso).</summary>
internal static class DatosDeReportes
{
    public static ValorizacionItemDto Item(int id, string codigo, string nombre, decimal stock, decimal costo, string categoria = "Construcción") =>
        new(id, codigo, nombre, categoria, stock, costo, stock * costo);

    public static readonly ValorizacionItemDto Cemento = Item(1, "MAT-0142", "Cemento portland 50 kg", 320m, 480m);
    public static readonly ValorizacionItemDto Hipoclorito = Item(8, "LIM-0455", "Hipoclorito de sodio 5 L", -3m, 210m, "Limpieza");

    public static ProductoDto Producto(int id, string nombre) => new(
        id, $"SKU{id}", null, nombre, null, null, null, null, 1, "Unidad", 0m, 0m, 0m, true, default);

    public static MovimientoHistorialDto Movimiento(int id, decimal anterior, decimal nuevo, string? comentario = null) =>
        new(id, 1, "Cemento portland 50 kg", TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, Math.Abs(nuevo - anterior), 0m, anterior, nuevo,
            comentario, new DateTime(2026, 9, 30, 19, 40, 0, DateTimeKind.Utc), 2, "Operador");
}
