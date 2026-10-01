using System;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Error de conversión de un número tipeado que NO cumple el formato es-UY (ej. "5.4"). Su
/// <see cref="Exception.Message"/> es un mensaje de DOMINIO armado por
/// <see cref="StockApp.Domain.Formato.FormatoEsUy.TryParseDecimal"/> (ej. "Usá coma para los
/// decimales: 5,4"), no un texto crudo de .NET: <see cref="ErrorValidacionConverter"/> lo deja
/// pasar tal cual en vez de reemplazarlo por el genérico.
/// </summary>
public sealed class EntradaNumericaInvalidaException(string mensaje) : FormatException(mensaje);
