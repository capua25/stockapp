using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StockApp.Application.Auditoria;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Documentos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Reportes;
using UglyToad.PdfPig;
using Xunit;

namespace StockApp.Presentation.Tests.Exportacion;

/// <summary>
/// Ejercita el exportador PDF REAL (<see cref="PdfExporterMigraDoc"/>, no un mock) contra los
/// DTOs REALES de las 9 pantallas del alcance (review final, Importante 5).
///
/// Hueco que cierra: las 9 pantallas mockean <see cref="IPdfExporter"/> y los tests de la
/// plantilla usan records inventados, así que NADIE verificaba que los
/// <c>ColumnasPdf.Propiedad</c> de cada pantalla resuelvan contra propiedades que EXISTEN en su
/// DTO. La plantilla resuelve por reflexión y tira <see cref="ArgumentException"/> si no
/// encuentra la propiedad: un rename en un DTO reventaba recién en producción, con toda la suite
/// en verde. Desde el fix de rótulos las 9 constantes usan <c>nameof(...)</c>, así que el
/// compilador ya cubre el caso del rename -- estos tests son la red que queda si alguien vuelve
/// a escribir un literal, y además verifican que el documento se genera de punta a punta.
///
/// Un test por pantalla (y no un único <c>[Fact]</c> con nueve llamadas) para que el reporte diga
/// CUÁL pantalla se rompió sin tener que leer el stack trace.
/// </summary>
public class ColumnasPdfContraDtosRealesTests
{
    private static readonly MetadatosDocumento Metadatos =
        new("Reporte", "Sin filtros aplicados.", "admin");

    private static readonly DateTime FechaFija = new(2026, 9, 18, 14, 33, 21, DateTimeKind.Utc);

    /// <summary>
    /// Afirma las dos cosas que el review pide: (1) cada propiedad declarada EXISTE en el DTO, y
    /// (2) el PDF se genera de verdad con el exportador real. La (1) se afirma explícitamente y
    /// no solo "porque no tiró excepción": así el mensaje de error nombra la propiedad y la
    /// pantalla en vez de dejar un <see cref="ArgumentException"/> suelto.
    /// </summary>
    private static void VerificarPantalla<T>(IReadOnlyList<ColumnaPdf> columnas, T fila)
    {
        Assert.NotEmpty(columnas);

        foreach (var columna in columnas)
        {
            var propiedad = typeof(T).GetProperty(
                columna.Propiedad, BindingFlags.Public | BindingFlags.Instance);

            Assert.True(
                propiedad is not null,
                $"La columna '{columna.Rotulo}' declara la propiedad '{columna.Propiedad}', " +
                $"que no existe en {typeof(T).Name}.");
        }

        Assert.All(columnas, columna => Assert.False(string.IsNullOrWhiteSpace(columna.Rotulo)));

        IPdfExporter exportador = new PdfExporterMigraDoc();

        var pdf = exportador.Exportar(new[] { fila }, columnas, Metadatos);

        using var documento = PdfDocument.Open(pdf);
        Assert.True(documento.NumberOfPages >= 1);
    }

    [Fact]
    public void Valorizacion_ColumnasPdf_ResuelvenContraElDtoRealYGeneranPdf() =>
        VerificarPantalla(
            ValorizacionViewModel.ColumnasPdf,
            new ValorizacionItemDto(1, "P001", "Azúcar", "Almacén", 22.5m, 45.5m, 1023.75m));

    [Fact]
    public void StockCategoria_ColumnasPdf_ResuelvenContraElDtoRealYGeneranPdf() =>
        VerificarPantalla(
            StockCategoriaViewModel.ColumnasPdf,
            new StockCategoriaDto("Almacén", 12, 340.5m, 26400m));

    [Fact]
    public void MasMovidos_ColumnasPdf_ResuelvenContraElDtoRealYGeneranPdf() =>
        VerificarPantalla(
            MasMovidosViewModel.ColumnasPdf,
            new MasMovidoDto(1, "P001", "Azúcar", 34, 512.25m));

    [Fact]
    public void HistorialPorProducto_ColumnasPdf_ResuelvenContraElDtoRealYGeneranPdf() =>
        VerificarPantalla(
            HistorialPorProductoViewModel.ColumnasPdf,
            new MovimientoHistorialDto(
                1, 1, "Azúcar", TipoMovimiento.Entrada, MotivoMovimiento.Compra,
                10m, 45.5m, 12.5m, 22.5m, "Ingreso por factura A-0001234", FechaFija, 1, "admin"));

    [Fact]
    public void AuditoriaLog_ColumnasPdf_ResuelvenContraElDtoRealYGeneranPdf() =>
        VerificarPantalla(
            AuditoriaLogViewModel.ColumnasPdf,
            new FilaAuditoriaPdf(
                FechaFija, "admin", TextoAccionAuditada.De(AccionAuditada.CambioPrecio), "Producto", 1,
                "PrecioCosto: 45,50 -> 48,00"));

    [Fact]
    public void ReporteTareas_ColumnasPdf_ResuelvenContraElDtoRealYGeneranPdf() =>
        VerificarPantalla(
            ReporteTareasViewModel.ColumnasPdf,
            new FilaReporteTareas("Centro", 1, 0, 2, 0, 3));

    private static PagoGasto PagoAutomaticoDeRentasGenerales()
    {
        var pago = PagoGasto.Automatico(FechaFija, 1234567.89m, "Pago contado (automático)", Caja.Local, 2);
        pago.FuenteFinanciamiento = new FuenteFinanciamiento { Id = 2, Nombre = "Rentas generales" };
        return pago;
    }

    [Fact]
    public void Gastos_ColumnasPdf_ResuelvenContraElDtoRealYGeneranPdf() =>
        VerificarPantalla(
            GastosViewModel.ColumnasPdf,
            new GastoFila(
                new Gasto
                {
                    Id = 1,
                    ProveedorId = 1,
                    Proveedor = new Proveedor { Id = 1, Nombre = "Ferretería del Centro S.R.L." },
                    NumeroFactura = "A-0001234",
                    Detalle = "Compra de materiales para el alumbrado público",
                    Fecha = FechaFija,
                    MontoTotal = 1234567.89m,
                    RubroGastoId = 3,
                    RubroGasto = new RubroGasto { Id = 3, Nombre = "Materiales de construcción" },
                    LineaPoaId = 4,
                    LineaPoa = new LineaPoa { Id = 4, Nombre = "Alumbrado público" },
                    // La fuente vive en el pago automático del contado, no en el gasto.
                    CondicionPago = CondicionPago.Contado,
                    Pagos = { PagoAutomaticoDeRentasGenerales() },
                },
                FechaFija));

    [Fact]
    public void Gastos_Pdf_IncluyeLaColumnaCajaAntesDeFuente_ConElNombreDeLaCajaDelPago()
    {
        var pago = PagoGasto.Automatico(FechaFija, 1234567.89m, "Pago contado (automático)", Caja.Poa, 5);
        pago.FuenteFinanciamiento = new FuenteFinanciamiento { Id = 5, Nombre = "Literal B", Caja = Caja.Poa };
        var gasto = new Gasto
        {
            Id = 1, Detalle = "Compra", Fecha = FechaFija, MontoTotal = 1234567.89m,
            CondicionPago = CondicionPago.Contado, Pagos = { pago },
        };
        var fila = new GastoFila(gasto, FechaFija);

        var rotulos = GastosViewModel.ColumnasPdf.Select(c => c.Rotulo).ToList();
        Assert.Equal(rotulos.IndexOf("Fuente") - 1, rotulos.IndexOf("Caja"));
        Assert.Contains(GastosViewModel.ColumnasPdf, c => c.Propiedad == "CajaTexto");

        var pdf = new PdfExporterMigraDoc().Exportar(new[] { fila }, GastosViewModel.ColumnasPdf, Metadatos);
        using var documento = PdfDocument.Open(pdf);
        var texto = string.Join(" ", documento.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
        Assert.Contains("Caja", texto);
        Assert.Contains("POA", texto);
        Assert.Contains("Literal", texto);
    }

    [Fact]
    public void LibroCaja_ColumnasPdf_ResuelvenContraElDtoRealYGeneranPdf() =>
        VerificarPantalla(
            LibroCajaViewModel.ColumnasPdf,
            new MovimientoCajaDto(
                new DateOnly(2026, 9, 18), "Egreso", "Pago factura A-0001234",
                "Ferretería del Centro S.R.L.", "A-0001234", "Rentas generales",
                "Materiales de construcción", 0m, 1234567.89m, -1234567.89m));

    [Fact]
    public void LibroCajaAnual_ColumnasPdf_ResuelvenContraElDtoRealYGeneranPdf() =>
        VerificarPantalla(
            LibroCajaViewModel.ColumnasPdfAnual,
            new FilaPdfMensual("Marzo", 1250000m, 1322750m, -72750m));

    /// <summary>El PDF anual con su resumen REAL (no un mock): vertical (4 columnas), con los saldos, los meses en
    /// déficit y los totales por rubro, y sin totales por fuente (spec, sección 2).</summary>
    [Fact]
    public void LibroCajaAnual_ConSuResumen_SaleVerticalConSaldosDeficitYRubros_SinFuentes()
    {
        var anual = new LibroCajaAnualDto(2026,
            [new TotalMensualDto(1, 1250000m, 980200m, 269800m), new TotalMensualDto(3, 1250000m, 1322750m, -72750m)],
            [new TotalPorClaveDto("Obras viales", 2302950m)]);
        IPdfExporter exportador = new PdfExporterMigraDoc();

        var pdf = exportador.Exportar(anual.TotalesPorMes, LibroCajaViewModel.ColumnasPdfAnual, Metadatos,
            LibroCajaViewModel.ArmarResumenAnual(anual, 812430.55m, 1009480.55m));

        using var documento = PdfDocument.Open(pdf);
        var primera = documento.GetPage(1);
        Assert.True(primera.Height > primera.Width, "el PDF anual tiene que salir vertical");
        var palabras = string.Join(" ", documento.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
        Assert.Contains("Saldo inicial", palabras);
        Assert.Contains("Meses con déficit", palabras);
        Assert.Contains("Marzo", palabras);
        Assert.Contains("Totales por rubro", palabras);
        Assert.DoesNotContain("fuente", palabras);
    }

    /// <summary>La TABLA (no la sección de déficit) lleva el nombre del mes: Enero es superávit, así que solo puede
    /// aparecer en el PDF si la fila de la tabla trae el nombre; con el número saldría "1".</summary>
    [Fact]
    public void LibroCajaAnual_FilasDeLaTabla_LlevanElNombreDelMes()
    {
        var filas = LibroCajaViewModel.ArmarFilasPdfAnual(
            [new TotalMensualDto(1, 1250000m, 980200m, 269800m), new TotalMensualDto(9, 10m, 40m, -30m)]);

        Assert.Equal("Enero", filas[0].Mes);
        Assert.Equal("Setiembre", filas[1].Mes);
        Assert.Equal(-30m, filas[1].Neto);

        var pdf = new PdfExporterMigraDoc().Exportar(filas, LibroCajaViewModel.ColumnasPdfAnual, Metadatos);
        using var documento = PdfDocument.Open(pdf);
        var palabras = documento.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text).ToList();
        Assert.Contains("Enero", palabras);
        Assert.Contains("Setiembre", palabras);
    }

    [Fact]
    public void ControlPoa_ColumnasPdf_ResuelvenContraElDtoRealYGeneranPdf() =>
        VerificarPantalla(
            ControlPoaViewModel.ColumnasPdf,
            new ControlPoaLineaDto(
                1, "Alumbrado público", "Programa 3", 2026, 2000000m, 1234567.89m, 765432.11m, 61.73m, false));
}
