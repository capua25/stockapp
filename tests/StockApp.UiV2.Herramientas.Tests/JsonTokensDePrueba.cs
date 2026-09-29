namespace StockApp.UiV2.Herramientas.Tests;

internal static class JsonTokensDePrueba
{
    public const string Valido = """
        {
          "prefijo": "V2",
          "factorAltoLinea": 1.20996,
          "colores": { "Acento": "#1D4ED8", "Fondo": "#F7F7F5" },
          "coloresDerivados": { "TextoSobreAcento": "#FFFFFF" },
          "radios": { "Control": 8 },
          "alturas": { "Control": 36 },
          "anchos": { "Sidebar": 240 },
          "espaciado": [4, 8],
          "sombraCard": { "x": 0, "y": 1, "desenfoque": 2, "expansion": 0, "color": "#000000", "opacidad": 0.05 },
          "tipografia": {
            "TituloPagina": { "tamano": 22, "peso": 600, "altoLinea": 26 },
            "HeaderColumna": { "tamano": 11, "peso": 600, "espaciadoLetras": 0.6, "mayusculas": true }
          }
        }
        """;
}
