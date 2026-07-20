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

    public PlanillaPoaOds ParsearPoa(Stream odsStream) => throw new NotImplementedException();

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
