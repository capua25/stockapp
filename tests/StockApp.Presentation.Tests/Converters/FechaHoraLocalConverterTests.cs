using System;
using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

/// <summary>
/// <see cref="FechaHoraLocalConverter"/>: instante UTC → texto "dd/MM/yyyy HH:mm" en hora local, con formato FIJO
/// (no depende de la cultura que le pasa el binding: por eso el test le pasa en-US, que pondría MM/dd).
/// </summary>
public class FechaHoraLocalConverterTests
{
    [Fact]
    public void UtcAHoraLocalConFormatoFijo()
    {
        var utc = new DateTime(2026, 10, 7, 14, 35, 0, DateTimeKind.Local).ToUniversalTime();

        Assert.Equal("07/10/2026 14:35", FechaHoraLocalConverter.Instance.Convert(utc, typeof(string), null, CultureInfo.GetCultureInfo("en-US")));
    }

    /// <summary>
    /// El DateTime puede volver de la deserialización/DB con Kind=Unspecified aunque sea un instante UTC: se interpreta
    /// como UTC. Ojo: DateTime.ToLocalTime() ya trata Unspecified como UTC, así que este caso no depende del
    /// SpecifyKind del conversor (fija el contrato, no esa línea). En una máquina con huso UTC tampoco distingue.
    /// </summary>
    [Fact]
    public void KindUnspecified_SeInterpretaComoUtc()
    {
        var utc = new DateTime(2026, 10, 7, 14, 35, 0, DateTimeKind.Local).ToUniversalTime();
        var sinKind = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);

        Assert.Equal("07/10/2026 14:35", FechaHoraLocalConverter.Instance.Convert(sinKind, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Null_DaVacio() =>
        Assert.Equal(string.Empty, FechaHoraLocalConverter.Instance.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
}
