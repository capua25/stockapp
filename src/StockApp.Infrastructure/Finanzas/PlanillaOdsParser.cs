using System.IO.Compression;
using System.Xml.Linq;
using StockApp.Application.Finanzas;

namespace StockApp.Infrastructure.Finanzas;

public sealed class PlanillaOdsParser : IPlanillaParser
{
    private static readonly string[] MesesGastos =
    {
        "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO",
        "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE",
    };

    public PlanillaGastosOds ParsearGastos(Stream odsStream)
    {
        var contentXml = LeerContentXml(odsStream);

        var filasPorMes = MesesGastos.ToDictionary(
            mes => mes,
            mes => (IReadOnlyList<FilaGastoOds>)ParsearHojaMesGastos(contentXml, mes));

        return new PlanillaGastosOds(filasPorMes, ParsearVariables(contentXml));
    }

    public PlanillaPoaOds ParsearPoa(Stream odsStream)
    {
        var contentXml = LeerContentXml(odsStream);

        var lineas = OdsContentXmlReader.ListarHojas(contentXml)
            .Where(nombre => nombre != "SALDO TOTALES")
            .Select(nombre => ParsearLineaPoa(contentXml, nombre))
            .ToList();

        return new PlanillaPoaOds(lineas, ParsearSaldosTotales(contentXml));
    }

    private static LineaPoaResumenOds ParsearLineaPoa(XDocument contentXml, string nombreHoja)
    {
        var hoja = OdsContentXmlReader.LeerHoja(contentXml, nombreHoja);

        var (filaEncabezadoDatos, colFactura) = hoja.BuscarTexto("FACTURA")
            ?? throw new InvalidOperationException($"La hoja '{nombreHoja}' no tiene columna FACTURA.");
        var colOrden = colFactura + 2;
        var colProveedor = colFactura + 4;
        var colGasto = colFactura + 6;
        var colImporte = colFactura + 10;

        var asignaciones = ParsearAsignaciones(hoja, nombreHoja, filaEncabezadoDatos);

        // Geometría real de cada hoja de línea (verificada contra las 15 hojas de
        // PlanillaPoa2026.ods):
        //   header FACTURA/ORDEN/... (fila ~11)
        //   0..N filas: movimientos reales, filas totalmente vacías, o filas con texto en
        //               columnas ajenas a los 5 campos tipados (ej. COMPOSTERAS)
        //   fila de TOTAL: SIEMPRE la última fila con contenido de la hoja
        //
        // Parte 1 — La fila de TOTAL se ubica por POSICIÓN, no por contenido: es la fila con
        // MAYOR índice que tenga alguna celda no vacía. Es indistinguible de un movimiento por
        // sus columnas (ej. CARPETA ASFÁLTICA tiene un movimiento legítimo con SOLO importe,
        // igual que el total), así que lo único que la separa es que está al fondo. Si tras el
        // header no hay ninguna fila con contenido, la hoja no tiene movimientos ni total.
        var filaTotal = -1;
        for (var f = filaEncabezadoDatos + 1; f < hoja.CantidadFilas; f++)
        {
            if (hoja.CeldasDeFila(f).Any())
                filaTotal = f;
        }

        if (filaTotal < 0)
            return new LineaPoaResumenOds(nombreHoja, asignaciones, new List<FilaPoaOds>());

        // Parte 2 — Los movimientos son las filas ENTRE el header y la fila de TOTAL (excluida)
        // que aportan contenido en al menos uno de los 5 campos tipados. Una fila vacía
        // intermedia (ej. las 2 de PRENSA antes de su dato) o con texto en columnas ajenas (la
        // "literal C"/"literal B" de COMPOSTERAS) se SALTA con continue — NUNCA corta la lectura
        // ni cuenta como movimiento. El loop sólo termina al llegar a filaTotal (ya excluida por
        // el rango): jamás hay un break sobre fila vacía.
        var movimientos = new List<FilaPoaOds>();
        for (var f = filaEncabezadoDatos + 1; f < filaTotal; f++)
        {
            var factura = hoja.Celda(f, colFactura).ComoTexto();
            var orden = hoja.Celda(f, colOrden).ComoTexto();
            var proveedor = hoja.Celda(f, colProveedor).Texto;
            var gasto = hoja.Celda(f, colGasto).Texto;
            var importe = hoja.Celda(f, colImporte).Numero;

            if (factura is null && orden is null && proveedor is null && gasto is null && importe is null)
                continue;

            movimientos.Add(new FilaPoaOds(nombreHoja, f + 1, factura, orden, proveedor, gasto, importe));
        }

        return new LineaPoaResumenOds(nombreHoja, asignaciones, movimientos);
    }

    /// <summary>
    /// Financiamiento mixto (F5b, caso real COMPOSTERAS): las asignaciones se derivan de las
    /// celdas "LITERAL X" (mayúscula, Ordinal — <see cref="BuscarLiteralEnFila"/> ya ignora las
    /// "literal C"/"literal B" en minúscula de la tabla de movimientos) que aparecen en la ZONA
    /// DE ENCABEZADO, es decir ANTES de la fila del header de movimientos ("FACTURA"). Cada fila
    /// con una celda LITERAL es una asignación; su presupuesto y saldo son las celdas numéricas
    /// de ESA MISMA fila bajo los headers PRESUPUESTO/SALDO más cercanos en columna a la celda
    /// LITERAL.
    ///
    /// Esto resuelve el financiamiento mixto sin necesidad de detectar el bloque AGREGADO como
    /// caso especial: en COMPOSTERAS el header trae DOS pares PRESUPUESTO/SALDO — uno agregado
    /// (col8/col10, la suma de los otros dos, SIN literal propio) y uno compartido por las dos
    /// filas de asignación real (col14/col15). Elegir siempre el par MÁS CERCANO en columna a la
    /// celda LITERAL de la fila descarta naturalmente el agregado (col8 queda a distancia 4 de
    /// LITERAL en col12, mientras que col14 queda a distancia 2) sin tener que reconocerlo por
    /// colspan ni por ausencia de literal.
    /// </summary>
    private static IReadOnlyList<AsignacionPoaOds> ParsearAsignaciones(
        OdsHoja hoja, string nombreHoja, int filaEncabezadoDatos)
    {
        var (filaPrimerPresupuesto, _) = hoja.BuscarTexto("PRESUPUESTO")
            ?? throw new InvalidOperationException($"La hoja '{nombreHoja}' no tiene celda PRESUPUESTO.");

        // Pares PRESUPUESTO/SALDO de la zona de encabezado, emparejados por proximidad: cada
        // columna PRESUPUESTO (ordenadas ascendente) se empareja con la columna SALDO en la
        // misma posición de la lista de SALDO (también ordenada ascendente) — válido porque cada
        // bloque PRESUPUESTO/SALDO aparece consecutivo y no se entrelaza con el siguiente bloque
        // (verificado contra las 15 hojas de PlanillaPoa2026.ods).
        var columnasPresupuesto = hoja.BuscarTodos("PRESUPUESTO")
            .Where(p => p.Fila < filaEncabezadoDatos)
            .Select(p => p.Columna)
            .OrderBy(c => c)
            .ToList();
        var columnasSaldo = hoja.BuscarTodos("SALDO")
            .Where(p => p.Fila < filaEncabezadoDatos)
            .Select(p => p.Columna)
            .OrderBy(c => c)
            .ToList();

        if (columnasPresupuesto.Count == 0 || columnasSaldo.Count != columnasPresupuesto.Count)
        {
            throw new InvalidOperationException(
                $"La hoja '{nombreHoja}' no tiene pares PRESUPUESTO/SALDO consistentes en el encabezado.");
        }

        var paresPresupuestoSaldo = columnasPresupuesto
            .Zip(columnasSaldo, (colPresupuesto, colSaldo) => (ColPresupuesto: colPresupuesto, ColSaldo: colSaldo))
            .ToList();

        var asignaciones = new List<AsignacionPoaOds>();
        for (var f = filaPrimerPresupuesto; f < filaEncabezadoDatos; f++)
        {
            var literalEnFila = BuscarLiteralEnFila(hoja, f);
            if (literalEnFila is null)
                continue;

            var (colLiteral, literal) = literalEnFila.Value;
            var par = paresPresupuestoSaldo
                .OrderBy(p => Math.Abs(p.ColPresupuesto - colLiteral))
                .First();

            var presupuesto = hoja.Celda(f, par.ColPresupuesto).Numero
                ?? throw new InvalidOperationException(
                    $"La hoja '{nombreHoja}' no tiene valor de PRESUPUESTO para LITERAL {literal}.");
            var saldo = hoja.Celda(f, par.ColSaldo).Numero
                ?? throw new InvalidOperationException(
                    $"La hoja '{nombreHoja}' no tiene valor de SALDO para LITERAL {literal}.");

            asignaciones.Add(new AsignacionPoaOds(literal, presupuesto, saldo));
        }

        if (asignaciones.Count == 0)
            throw new InvalidOperationException($"La hoja '{nombreHoja}' no tiene ninguna celda LITERAL.");

        return asignaciones;
    }

    private static (int Columna, string Literal)? BuscarLiteralEnFila(OdsHoja hoja, int fila) =>
        hoja.CeldasDeFila(fila)
            .Where(c => c.Celda.Texto is not null && c.Celda.Texto.StartsWith("LITERAL ", StringComparison.Ordinal))
            .Select(c => ((int Columna, string Literal)?)(c.Columna, c.Celda.Texto![8..].Trim()))
            .FirstOrDefault();

    private static SaldosTotalesPoaOds ParsearSaldosTotales(XDocument contentXml)
    {
        var hoja = OdsContentXmlReader.LeerHoja(contentXml, "SALDO TOTALES");

        var (filaLiteralB, colLiteralB) = hoja.BuscarTexto("SALDO LITERAL B")
            ?? throw new InvalidOperationException("La hoja 'SALDO TOTALES' no tiene celda SALDO LITERAL B.");
        var (_, colLiteralC) = hoja.BuscarTexto("SALDO LITERAL C")
            ?? throw new InvalidOperationException("La hoja 'SALDO TOTALES' no tiene celda SALDO LITERAL C.");

        // Gotcha verificado contra la planilla real: la etiqueta ocupa 2 filas fusionadas
        // (rowspan=2), después hay 1 fila separadora, y el VALOR (también rowspan=2) está 3
        // filas más abajo, en la MISMA columna que la etiqueta.
        const int desplazamientoEtiquetaAValor = 3;
        var filaValor = filaLiteralB + desplazamientoEtiquetaAValor;

        var saldoB = hoja.Celda(filaValor, colLiteralB).Numero
            ?? throw new InvalidOperationException("No se pudo leer el valor de SALDO LITERAL B.");
        var saldoC = hoja.Celda(filaValor, colLiteralC).Numero
            ?? throw new InvalidOperationException("No se pudo leer el valor de SALDO LITERAL C.");

        return new SaldosTotalesPoaOds(saldoB, saldoC);
    }

    private static XDocument LeerContentXml(Stream odsStream)
    {
        using var zip = new ZipArchive(odsStream, ZipArchiveMode.Read, leaveOpen: true);
        var entrada = zip.GetEntry("content.xml")
            ?? throw new InvalidOperationException("El archivo no es un .ods válido: falta content.xml.");
        using var contentStream = entrada.Open();
        return XDocument.Load(contentStream);
    }

    private static IReadOnlyList<FilaGastoOds> ParsearHojaMesGastos(XDocument contentXml, string nombreHoja)
    {
        var hoja = OdsContentXmlReader.LeerHoja(contentXml, nombreHoja);

        var (filaEncabezado, colFecha) = hoja.BuscarTexto("FECHA")
            ?? throw new InvalidOperationException($"La hoja '{nombreHoja}' no tiene columna FECHA.");

        var colFactura = colFecha + 1;
        var colOrden = colFecha + 2;
        var colProveedor = colFecha + 3;
        var colDestino = colFecha + 4;
        var colGasto = colFecha + 5;
        var colIngreso = colFecha + 6;
        var colEgreso = colFecha + 7;
        var colSaldo = colFecha + 8;
        var colLiteral = colFecha + 9;
        var colCodigo = colFecha + 10;
        var colRubro = colFecha + 11;

        var filas = new List<FilaGastoOds>();
        for (var f = filaEncabezado + 1; f < hoja.CantidadFilas; f++)
        {
            var fecha = hoja.Celda(f, colFecha).Fecha;
            var factura = hoja.Celda(f, colFactura).ComoTexto();
            var orden = hoja.Celda(f, colOrden).ComoTexto();
            var proveedor = hoja.Celda(f, colProveedor).Texto;
            var destino = hoja.Celda(f, colDestino).Texto;
            var gasto = hoja.Celda(f, colGasto).Texto;
            var ingreso = hoja.Celda(f, colIngreso).Numero;
            var egreso = hoja.Celda(f, colEgreso).Numero;

            var esMovimiento = fecha is not null || factura is not null || orden is not null
                || proveedor is not null || destino is not null || gasto is not null
                || ingreso is not null || egreso is not null;
            if (!esMovimiento)
                continue;

            filas.Add(new FilaGastoOds(
                Hoja: nombreHoja,
                NumeroFila: f + 1, // 1-based, como lo ve un humano en LibreOffice
                Fecha: fecha,
                Factura: factura,
                Orden: orden,
                Proveedor: proveedor,
                Destino: destino,
                Gasto: gasto,
                Ingreso: ingreso,
                Egreso: egreso,
                Saldo: hoja.Celda(f, colSaldo).Numero,
                Literal: hoja.Celda(f, colLiteral).Texto,
                Codigo: hoja.Celda(f, colCodigo).Numero is { } cod ? (int)cod : null,
                Rubro: hoja.Celda(f, colRubro).Texto));
        }

        return filas;
    }

    private static IReadOnlyList<LineaVariableOds> ParsearVariables(XDocument contentXml)
    {
        var hoja = OdsContentXmlReader.LeerHoja(contentXml, "Variables");
        var (filaEncabezado, colLiteral) = hoja.BuscarTexto("LITERAL")
            ?? throw new InvalidOperationException("La hoja 'Variables' no tiene columna LITERAL.");
        var colCodigo = colLiteral + 1;
        var colRubro = colLiteral + 2;

        var lineas = new List<LineaVariableOds>();
        for (var f = filaEncabezado + 1; f < hoja.CantidadFilas; f++)
        {
            var literal = hoja.Celda(f, colLiteral).Texto;
            var codigo = hoja.Celda(f, colCodigo).Numero;
            var rubro = hoja.Celda(f, colRubro).Texto;
            if (literal is null || codigo is null || rubro is null)
                continue;

            lineas.Add(new LineaVariableOds(literal, (int)codigo.Value, rubro));
        }

        return lineas;
    }
}
