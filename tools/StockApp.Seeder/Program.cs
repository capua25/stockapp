using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Seeder;

/// <summary>
/// Herramienta de consola para sembrar la base de datos de StockApp con datos de ejemplo
/// en todas las tablas. Uso:
///   dotnet run --project tools/StockApp.Seeder                              -> siembra la base local por defecto
///   dotnet run --project tools/StockApp.Seeder -- --connection "Host=..."   -> siembra una base alternativa
///   dotnet run --project tools/StockApp.Seeder -- --reset                   -> limpia todas las tablas antes de sembrar
/// Por defecto la siembra es aditiva e idempotente: no duplica catálogos/usuarios/productos
/// ya existentes (se identifican por su campo único), y solo genera movimientos/logs para
/// las entidades creadas en la corrida actual.
/// </summary>
public static class Program
{
    private const string ConnectionStringDefault =
        "Host=localhost;Port=5432;Database=stockapp;Username=stockapp;Password=stockapp";

    public static async Task<int> Main(string[] args)
    {
        var reset = args.Contains("--reset");
        var connectionString = ResolverConnectionString(args);

        Console.WriteLine($"Base de datos: {connectionString}");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var ctx = new AppDbContext(options);

        await ctx.Database.MigrateAsync();

        if (reset)
        {
            await ResetearAsync(ctx);
        }

        var resumen = new List<string>();
        IPasswordHasher hasher = new BcryptPasswordHasher();

        var (unidadesMapa, unidadesNuevas) = await SembrarUnidadesAsync(ctx, resumen);
        var (usuariosMapa, usuariosNuevos) = await SembrarUsuariosAsync(ctx, hasher, resumen);
        var (categoriasMapa, categoriasNuevas) = await SembrarCategoriasAsync(ctx, resumen);
        var (proveedoresMapa, proveedoresNuevos) = await SembrarProveedoresAsync(ctx, resumen);
        var productosNuevos = await SembrarProductosAsync(ctx, unidadesMapa, categoriasMapa, proveedoresMapa, resumen);
        await SembrarMovimientosAsync(ctx, productosNuevos, usuariosMapa, resumen);
        await SembrarLogsAsync(ctx, usuariosMapa["admin"], unidadesNuevas, usuariosNuevos, categoriasNuevas, proveedoresNuevos, productosNuevos, resumen);

        Console.WriteLine();
        Console.WriteLine("== Resumen de siembra ==");
        foreach (var linea in resumen)
        {
            Console.WriteLine($"  - {linea}");
        }

        Console.WriteLine();
        Console.WriteLine("=======================================");
        Console.WriteLine("  CREDENCIALES DE USUARIOS SEMBRADOS");
        Console.WriteLine("=======================================");
        Console.WriteLine("  Usuario: admin      | Contraseña: admin123    | Rol: Admin");
        Console.WriteLine("  Usuario: operador   | Contraseña: operador123 | Rol: Operador");
        Console.WriteLine("=======================================");

        return 0;
    }

    private static string ResolverConnectionString(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--connection" && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return ConnectionStringDefault;
    }

    private static async Task ResetearAsync(AppDbContext ctx)
    {
        await ctx.LogsAuditoria.ExecuteDeleteAsync();
        await ctx.MovimientosStock.ExecuteDeleteAsync();
        await ctx.Productos.ExecuteDeleteAsync();
        await ctx.Proveedores.ExecuteDeleteAsync();
        await ctx.Categorias.ExecuteDeleteAsync();
        await ctx.Usuarios.ExecuteDeleteAsync();
        await ctx.UnidadesMedida.ExecuteDeleteAsync();
        Console.WriteLine("Reset: todas las tablas fueron limpiadas antes de sembrar.");
    }

    // ── UnidadMedida ─────────────────────────────────────────────────────────

    private static readonly (string Nombre, string Abreviatura)[] UnidadesData =
    [
        ("Unidad", "u"),
        ("Kilogramo", "kg"),
        ("Litro", "l"),
        ("Metro", "m"),
        ("Caja", "cj"),
        ("Paquete", "paq"),
    ];

    private static async Task<(Dictionary<string, UnidadMedida> Mapa, List<UnidadMedida> Nuevas)> SembrarUnidadesAsync(
        AppDbContext ctx, List<string> resumen)
    {
        var existentes = await ctx.UnidadesMedida.ToListAsync();
        var mapa = existentes.ToDictionary(u => u.Nombre, u => u, StringComparer.OrdinalIgnoreCase);
        var nuevas = new List<UnidadMedida>();

        foreach (var (nombre, abreviatura) in UnidadesData)
        {
            if (mapa.ContainsKey(nombre)) continue;

            var nueva = new UnidadMedida { Nombre = nombre, Abreviatura = abreviatura, Activo = true };
            ctx.UnidadesMedida.Add(nueva);
            mapa[nombre] = nueva;
            nuevas.Add(nueva);
        }

        if (nuevas.Count > 0) await ctx.SaveChangesAsync();
        resumen.Add($"Unidades de medida: {nuevas.Count} creadas / {existentes.Count} existentes");
        return (mapa, nuevas);
    }

    // ── Usuario ──────────────────────────────────────────────────────────────

    private sealed record UsuarioSeed(string NombreUsuario, string NombreCompleto, string Password, RolUsuario Rol);

    private static readonly UsuarioSeed[] UsuariosData =
    [
        new("admin", "Administrador", "admin123", RolUsuario.Admin),
        new("operador", "Operador de Depósito", "operador123", RolUsuario.Operador),
    ];

    private static async Task<(Dictionary<string, Usuario> Mapa, List<Usuario> Nuevos)> SembrarUsuariosAsync(
        AppDbContext ctx, IPasswordHasher hasher, List<string> resumen)
    {
        var existentes = await ctx.Usuarios.ToListAsync();
        var mapa = existentes.ToDictionary(u => u.NombreUsuario, u => u, StringComparer.OrdinalIgnoreCase);
        var nuevos = new List<Usuario>();

        foreach (var seed in UsuariosData)
        {
            if (mapa.ContainsKey(seed.NombreUsuario)) continue;

            var nuevo = new Usuario
            {
                NombreUsuario = seed.NombreUsuario,
                NombreCompleto = seed.NombreCompleto,
                HashContrasena = hasher.Hash(seed.Password),
                Rol = seed.Rol,
                Activo = true,
                FechaAlta = DateTime.UtcNow.AddDays(-120),
            };
            ctx.Usuarios.Add(nuevo);
            mapa[seed.NombreUsuario] = nuevo;
            nuevos.Add(nuevo);
        }

        if (nuevos.Count > 0) await ctx.SaveChangesAsync();
        resumen.Add($"Usuarios: {nuevos.Count} creados / {existentes.Count} existentes");
        return (mapa, nuevos);
    }

    // ── Categoria ────────────────────────────────────────────────────────────

    private static readonly string[] CategoriasData =
        ["Bebidas", "Almacén", "Limpieza", "Lácteos", "Panadería", "Congelados"];

    private static async Task<(Dictionary<string, Categoria> Mapa, List<Categoria> Nuevas)> SembrarCategoriasAsync(
        AppDbContext ctx, List<string> resumen)
    {
        var existentes = await ctx.Categorias.ToListAsync();
        var mapa = existentes.ToDictionary(c => c.Nombre, c => c, StringComparer.OrdinalIgnoreCase);
        var nuevas = new List<Categoria>();

        foreach (var nombre in CategoriasData)
        {
            if (mapa.ContainsKey(nombre)) continue;

            var nueva = new Categoria { Nombre = nombre, Activo = true };
            ctx.Categorias.Add(nueva);
            mapa[nombre] = nueva;
            nuevas.Add(nueva);
        }

        if (nuevas.Count > 0) await ctx.SaveChangesAsync();
        resumen.Add($"Categorías: {nuevas.Count} creadas / {existentes.Count} existentes");
        return (mapa, nuevas);
    }

    // ── Proveedor ────────────────────────────────────────────────────────────

    private sealed record ProveedorSeed(string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas);

    private static readonly ProveedorSeed[] ProveedoresData =
    [
        new("Distribuidora Norte S.A.", "+54 11 4555-1234", "ventas@distnorte.com.ar", "Av. Corrientes 1234, CABA", "Proveedor histórico, entrega semanal"),
        new("Almacén Mayorista del Sur", "+54 11 4666-5678", null, "Ruta 3 Km 45, La Matanza", null),
        new("Lácteos del Valle", null, "pedidos@lacteosvalle.com.ar", "Camino Rural 8, Cañuelas", "Solo productos refrigerados"),
        new("Proveedor Express S.R.L.", "+54 11 4777-9012", "contacto@provexpress.com.ar", null, null),
        new("Insumos del Litoral", null, null, null, null),
    ];

    private static async Task<(Dictionary<string, Proveedor> Mapa, List<Proveedor> Nuevos)> SembrarProveedoresAsync(
        AppDbContext ctx, List<string> resumen)
    {
        var existentes = await ctx.Proveedores.ToListAsync();
        var mapa = existentes.ToDictionary(p => p.Nombre, p => p, StringComparer.OrdinalIgnoreCase);
        var nuevos = new List<Proveedor>();

        foreach (var seed in ProveedoresData)
        {
            if (mapa.ContainsKey(seed.Nombre)) continue;

            var nuevo = new Proveedor
            {
                Nombre = seed.Nombre,
                Telefono = seed.Telefono,
                Email = seed.Email,
                Direccion = seed.Direccion,
                Notas = seed.Notas,
                Activo = true,
            };
            ctx.Proveedores.Add(nuevo);
            mapa[seed.Nombre] = nuevo;
            nuevos.Add(nuevo);
        }

        if (nuevos.Count > 0) await ctx.SaveChangesAsync();
        resumen.Add($"Proveedores: {nuevos.Count} creados / {existentes.Count} existentes");
        return (mapa, nuevos);
    }

    // ── Producto ─────────────────────────────────────────────────────────────

    private sealed record ProductoSeed(
        string Codigo,
        string? CodigoBarras,
        string Nombre,
        string? Descripcion,
        string? CategoriaNombre,
        string? ProveedorNombre,
        string UnidadNombre,
        decimal PrecioCosto,
        decimal PrecioVenta,
        decimal StockMinimo,
        decimal StockFinal,
        int DiasAltaAtras);

    private static readonly ProductoSeed[] ProductosData =
    [
        new("COD-0001", "7791234560017", "Coca-Cola 1.5L", "Gaseosa cola línea familiar", "Bebidas", "Distribuidora Norte S.A.", "Unidad", 800m, 1400m, 10m, 45m, 85),
        new("COD-0002", null, "Agua Mineral Villa del Sur 2L", "Agua sin gas", "Bebidas", "Distribuidora Norte S.A.", "Unidad", 350m, 650m, 15m, 8m, 60),
        new("COD-0003", "7791234560031", "Cerveza Quilmes 1L", "Retornable", "Bebidas", null, "Unidad", 500m, 900m, 12m, 30m, 40),
        new("COD-0004", null, "Arroz Gallo Oro 1kg", "Arroz largo fino", "Almacén", "Almacén Mayorista del Sur", "Kilogramo", 900m, 1500m, 20m, 60m, 100),
        new("COD-0005", "7791234560055", "Fideos Matarazzo 500g", "Fideos tipo tallarín", "Almacén", "Almacén Mayorista del Sur", "Paquete", 400m, 750m, 25m, 5m, 70),
        new("COD-0006", null, "Aceite Natura 900ml", "Aceite de girasol", "Almacén", null, "Litro", 1200m, 2100m, 10m, 22m, 55),
        new("COD-0007", "7791234560079", "Detergente Magistral 750ml", "Lavavajilla", "Limpieza", "Proveedor Express S.R.L.", "Litro", 600m, 1100m, 8m, 18m, 30),
        new("COD-0008", null, "Lavandina Ayudín 1L", "Lavandina concentrada", "Limpieza", "Proveedor Express S.R.L.", "Litro", 300m, 580m, 15m, 40m, 90),
        new("COD-0009", null, "Jabón en Polvo Skip 3kg", "Jabón en polvo para ropa", "Limpieza", null, "Kilogramo", 2500m, 4200m, 6m, 3m, 65),
        new("COD-0010", "7791234560103", "Leche Entera La Serenísima 1L", "Leche entera fresca", "Lácteos", "Lácteos del Valle", "Litro", 450m, 780m, 20m, 50m, 20),
        new("COD-0011", null, "Yogur Ser Frutilla 190g", "Yogur bebible", "Lácteos", "Lácteos del Valle", "Unidad", 200m, 380m, 30m, 12m, 15),
        new("COD-0012", "7791234560127", "Queso Cremoso La Paulina 1kg", "Queso cremoso a granel", "Lácteos", "Lácteos del Valle", "Kilogramo", 3800m, 6200m, 5m, 14m, 45),
        new("COD-0013", null, "Pan Lactal Bimbo 500g", "Pan de molde", "Panadería", null, "Paquete", 700m, 1250m, 15m, 25m, 10),
        new("COD-0014", "7791234560141", "Facturas Surtidas x6", "Docena de facturas variadas", "Panadería", "Insumos del Litoral", "Caja", 1500m, 2800m, 8m, 6m, 5),
        new("COD-0015", null, "Galletitas Oreo 118g", "Galletitas rellenas de chocolate", "Panadería", "Insumos del Litoral", "Paquete", 550m, 980m, 20m, 35m, 50),
        new("COD-0016", "7791234560165", "Hamburguesas Paty x4", "Hamburguesas congeladas de carne", "Congelados", "Insumos del Litoral", "Caja", 1800m, 3100m, 10m, 28m, 35),
        new("COD-0017", null, "Papas Fritas McCain 1kg", "Papas prefritas congeladas", "Congelados", null, "Kilogramo", 1600m, 2900m, 12m, 4m, 25),
        new("COD-0018", "7791234560189", "Pilas AA Duracell x4", "Pilas alcalinas", null, "Proveedor Express S.R.L.", "Paquete", 900m, 1600m, 10m, 20m, 60),
        new("COD-0019", null, "Bolsas de Residuo 50u", "Bolsas negras reforzadas", null, null, "Paquete", 500m, 950m, 15m, 45m, 75),
        new("COD-0020", "7791234560202", "Cinta Adhesiva Ancha", "Cinta de embalar", null, "Insumos del Litoral", "Unidad", 250m, 500m, 20m, 9m, 95),
    ];

    private static async Task<List<Producto>> SembrarProductosAsync(
        AppDbContext ctx,
        Dictionary<string, UnidadMedida> unidades,
        Dictionary<string, Categoria> categorias,
        Dictionary<string, Proveedor> proveedores,
        List<string> resumen)
    {
        var existentes = await ctx.Productos.ToListAsync();
        var codigosExistentes = existentes.Select(p => p.Codigo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nuevos = new List<Producto>();

        foreach (var seed in ProductosData)
        {
            if (codigosExistentes.Contains(seed.Codigo)) continue;

            var producto = new Producto
            {
                Codigo = seed.Codigo,
                CodigoBarras = seed.CodigoBarras,
                Nombre = seed.Nombre,
                Descripcion = seed.Descripcion,
                Categoria = seed.CategoriaNombre is null ? null : categorias[seed.CategoriaNombre],
                Proveedor = seed.ProveedorNombre is null ? null : proveedores[seed.ProveedorNombre],
                UnidadMedida = unidades[seed.UnidadNombre],
                PrecioCosto = seed.PrecioCosto,
                PrecioVenta = seed.PrecioVenta,
                StockMinimo = seed.StockMinimo,
                StockActual = seed.StockFinal,
                Activo = true,
                FechaAlta = DateTime.UtcNow.AddDays(-seed.DiasAltaAtras),
            };

            ctx.Productos.Add(producto);
            nuevos.Add(producto);
        }

        if (nuevos.Count > 0) await ctx.SaveChangesAsync();
        resumen.Add($"Productos: {nuevos.Count} creados / {existentes.Count} existentes");
        return nuevos;
    }

    // ── MovimientoStock ──────────────────────────────────────────────────────
    // Plan de movimientos por Código de producto: cada entrada deja el StockActual
    // sembrado coherente con la suma (entradas - salidas). El primer movimiento de
    // cada producto es siempre una Entrada que cubre todas las salidas posteriores,
    // por lo que el stock nunca queda negativo en ningún punto intermedio.

    private sealed record MovimientoSeed(int DiasAtras, TipoMovimiento Tipo, MotivoMovimiento Motivo, decimal Cantidad, string? Comentario);

    private static readonly Dictionary<string, List<MovimientoSeed>> MovimientosPorCodigo = new()
    {
        // Un solo movimiento (Entrada = stock final)
        ["COD-0001"] = [new(82, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 45m, "Compra a proveedor")],
        ["COD-0002"] = [new(57, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 8m, null)],
        ["COD-0003"] = [new(37, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 30m, "Reposición de góndola")],
        ["COD-0004"] = [new(96, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 60m, null)],
        ["COD-0005"] = [new(66, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 5m, "Compra a proveedor")],
        ["COD-0006"] = [new(51, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 22m, null)],
        ["COD-0007"] = [new(27, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 18m, "Compra a proveedor")],
        ["COD-0008"] = [new(85, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 40m, null)],

        // Dos movimientos (Entrada + Salida) que cuadran al stock final
        ["COD-0009"] = [new(60, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 18m, null), new(40, TipoMovimiento.Salida, MotivoMovimiento.Venta, 15m, "Venta mostrador")],
        ["COD-0010"] = [new(17, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 70m, "Compra a proveedor"), new(8, TipoMovimiento.Salida, MotivoMovimiento.Venta, 20m, null)],
        ["COD-0011"] = [new(12, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 30m, null), new(5, TipoMovimiento.Salida, MotivoMovimiento.Venta, 18m, "Venta mostrador")],
        ["COD-0012"] = [new(40, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 30m, null), new(20, TipoMovimiento.Salida, MotivoMovimiento.Ajuste, 16m, "Ajuste de inventario tras conteo físico")],
        ["COD-0013"] = [new(8, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 40m, "Compra a proveedor"), new(3, TipoMovimiento.Salida, MotivoMovimiento.Venta, 15m, null)],
        ["COD-0014"] = [new(3, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 16m, null), new(1, TipoMovimiento.Salida, MotivoMovimiento.Venta, 10m, "Venta mostrador")],
        ["COD-0015"] = [new(45, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 55m, "Compra a proveedor"), new(25, TipoMovimiento.Salida, MotivoMovimiento.Venta, 20m, null)],
        ["COD-0016"] = [new(30, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 45m, null), new(15, TipoMovimiento.Salida, MotivoMovimiento.Venta, 17m, "Venta mostrador")],
        ["COD-0017"] = [new(21, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 16m, null), new(10, TipoMovimiento.Salida, MotivoMovimiento.Merma, 12m, "Producto vencido dado de baja")],
        ["COD-0018"] = [new(55, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 38m, "Compra a proveedor"), new(30, TipoMovimiento.Salida, MotivoMovimiento.Venta, 18m, null)],

        // Tres movimientos (Entrada + Salida-Venta + Salida-Merma)
        ["COD-0019"] = [new(70, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 75m, "Compra a proveedor"), new(45, TipoMovimiento.Salida, MotivoMovimiento.Venta, 20m, null), new(20, TipoMovimiento.Salida, MotivoMovimiento.Merma, 10m, "Producto dañado en depósito")],
        ["COD-0020"] = [new(88, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 32m, null), new(50, TipoMovimiento.Salida, MotivoMovimiento.Venta, 15m, "Venta mostrador"), new(15, TipoMovimiento.Salida, MotivoMovimiento.Merma, 8m, "Producto dañado en depósito")],
    };

    private static async Task SembrarMovimientosAsync(
        AppDbContext ctx,
        List<Producto> productosNuevos,
        Dictionary<string, Usuario> usuarios,
        List<string> resumen)
    {
        if (productosNuevos.Count == 0)
        {
            resumen.Add("Movimientos de stock: 0 creados (no hay productos nuevos en esta corrida)");
            return;
        }

        var admin = usuarios["admin"];
        var operador = usuarios["operador"];

        var creados = 0;
        var productIndex = 0;
        foreach (var producto in productosNuevos)
        {
            if (!MovimientosPorCodigo.TryGetValue(producto.Codigo, out var plan))
            {
                productIndex++;
                continue;
            }

            var usuarioAsignado = productIndex % 2 == 0 ? admin : operador;
            foreach (var mov in plan)
            {
                var precioUnitario = mov.Tipo == TipoMovimiento.Entrada
                    ? producto.PrecioCosto
                    : mov.Motivo == MotivoMovimiento.Venta ? producto.PrecioVenta : producto.PrecioCosto;

                ctx.MovimientosStock.Add(new MovimientoStock
                {
                    Producto = producto,
                    Usuario = usuarioAsignado,
                    Tipo = mov.Tipo,
                    Cantidad = mov.Cantidad,
                    PrecioUnitario = precioUnitario,
                    Fecha = DateTime.UtcNow.AddDays(-mov.DiasAtras),
                    Motivo = mov.Motivo,
                    Comentario = mov.Comentario,
                });
                creados++;
            }

            productIndex++;
        }

        if (creados > 0) await ctx.SaveChangesAsync();
        resumen.Add($"Movimientos de stock: {creados} creados");
    }

    // ── LogAuditoria ─────────────────────────────────────────────────────────
    // Solo se registran logs para entidades creadas en esta corrida (naturalmente
    // idempotente: en corridas posteriores no hay entidades nuevas, no hay logs nuevos).

    private static async Task SembrarLogsAsync(
        AppDbContext ctx,
        Usuario admin,
        List<UnidadMedida> unidadesNuevas,
        List<Usuario> usuariosNuevos,
        List<Categoria> categoriasNuevas,
        List<Proveedor> proveedoresNuevos,
        List<Producto> productosNuevos,
        List<string> resumen)
    {
        var momento = DateTime.UtcNow;
        var offsetMinutos = 0;
        var creados = 0;

        DateTime SiguienteFecha() => momento.AddMinutes(-(offsetMinutos++));

        foreach (var unidad in unidadesNuevas)
        {
            ctx.LogsAuditoria.Add(new LogAuditoria
            {
                UsuarioId = admin.Id,
                Fecha = SiguienteFecha(),
                Accion = AccionAuditada.AltaUnidadMedida,
                Entidad = "UnidadMedida",
                EntidadId = unidad.Id,
                Detalle = $"Unidad de medida '{unidad.Nombre} ({unidad.Abreviatura})' creada",
            });
            creados++;
        }

        foreach (var usuario in usuariosNuevos)
        {
            ctx.LogsAuditoria.Add(new LogAuditoria
            {
                UsuarioId = admin.Id,
                Fecha = SiguienteFecha(),
                Accion = AccionAuditada.AltaUsuario,
                Entidad = "Usuario",
                EntidadId = usuario.Id,
                Detalle = $"Usuario '{usuario.NombreUsuario}' creado con rol {usuario.Rol}",
            });
            creados++;
        }

        foreach (var categoria in categoriasNuevas)
        {
            ctx.LogsAuditoria.Add(new LogAuditoria
            {
                UsuarioId = admin.Id,
                Fecha = SiguienteFecha(),
                Accion = AccionAuditada.AltaCategoria,
                Entidad = "Categoria",
                EntidadId = categoria.Id,
                Detalle = $"Categoría '{categoria.Nombre}' creada",
            });
            creados++;
        }

        foreach (var proveedor in proveedoresNuevos)
        {
            ctx.LogsAuditoria.Add(new LogAuditoria
            {
                UsuarioId = admin.Id,
                Fecha = SiguienteFecha(),
                Accion = AccionAuditada.AltaProveedor,
                Entidad = "Proveedor",
                EntidadId = proveedor.Id,
                Detalle = $"Proveedor '{proveedor.Nombre}' creado",
            });
            creados++;
        }

        foreach (var producto in productosNuevos)
        {
            ctx.LogsAuditoria.Add(new LogAuditoria
            {
                UsuarioId = admin.Id,
                Fecha = SiguienteFecha(),
                Accion = AccionAuditada.AltaProducto,
                Entidad = "Producto",
                EntidadId = producto.Id,
                Detalle = $"Producto '{producto.Codigo} - {producto.Nombre}' creado con stock inicial {producto.StockActual}",
            });
            creados++;
        }

        if (creados > 0) await ctx.SaveChangesAsync();
        resumen.Add($"Logs de auditoría: {creados} creados");
    }
}
