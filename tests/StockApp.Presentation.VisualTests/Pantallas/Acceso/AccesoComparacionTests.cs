using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Acceso;

[Trait("Categoria", "ComparacionVisual")]
public class AccesoComparacionTests
{
    // P5: la marca de agua (opacidad 0.28 + escala 0.85 de un PNG) no se replica igual entre Skia y
    // Chromium, y un UmbralAprobado para ella dejaría ciega a toda la pantalla (spec §6.5). Se oculta
    // en AMBOS renders y la custodia LoginMarcaAguaTests. Valores calibrados, sin UmbralAprobado.
    private static readonly string[] OcultarMarcaAgua = [".marca-agua"];

    private static LoginView LoginSinMarcaAgua()
    {
        var vista = new LoginView();
        vista.FindControl<Image>("MarcaAgua")!.IsVisible = false;
        return vista;
    }

    private static void Comparar(Avalonia.Controls.Control vista, object vm, string mockup, TamanoLienzo? tamano = null) =>
        ComparacionVisual.CompararConMockup(vista, vm, mockup, tamano: tamano, ocultarEnMockup: OcultarMarcaAgua);

    [AvaloniaFact]
    public void Login_IgualAlMockup() =>
        Comparar(LoginSinMarcaAgua(), DatosAcceso.Login(), "acceso/login.html");

    [AvaloniaFact]
    public void Login_IgualAlMockup_1366x768() =>
        Comparar(LoginSinMarcaAgua(), DatosAcceso.Login(), "acceso/login.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void LoginConError_IgualAlMockup() =>
        Comparar(LoginSinMarcaAgua(), DatosAcceso.Login(usuario: "mperez", error: "Usuario o contraseña incorrectos."),
            "acceso/login-error.html");

    [AvaloniaFact]
    public void LoginConError_IgualAlMockup_1366x768() =>
        Comparar(LoginSinMarcaAgua(), DatosAcceso.Login(usuario: "mperez", error: "Usuario o contraseña incorrectos."),
            "acceso/login-error.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void LoginAccesoLimitado_IgualAlMockup() =>
        Comparar(LoginSinMarcaAgua(), DatosAcceso.Login(soloAccesoLimitado: true), "acceso/login-acceso-limitado.html");

    [AvaloniaFact]
    public void LoginAccesoLimitado_IgualAlMockup_1366x768() =>
        Comparar(LoginSinMarcaAgua(), DatosAcceso.Login(soloAccesoLimitado: true), "acceso/login-acceso-limitado.html",
            TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void Reset_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ResetAdminView(), DatosAcceso.Reset(), "acceso/reset.html");

    [AvaloniaFact]
    public void Reset_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new ResetAdminView(), DatosAcceso.Reset(), "acceso/reset.html",
            tamano: TamanoLienzo.Municipio);

    // Deuda de la Fase 1: el Margin 0,1,0,0 de la card del Reset depende de la paridad de su alto; estos dos
    // estados la cambian (o no) al mostrar una alerta.
    [AvaloniaFact]
    public void ResetCompletado_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ResetAdminView(), DatosAcceso.Reset(completado: true), "acceso/reset-completado.html");

    [AvaloniaFact]
    public void ResetCompletado_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new ResetAdminView(), DatosAcceso.Reset(completado: true), "acceso/reset-completado.html",
            tamano: TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ResetConError_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ResetAdminView(), DatosAcceso.Reset(error: "No se pudo resetear el Admin."),
            "acceso/reset-error.html");

    [AvaloniaFact]
    public void ResetConError_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new ResetAdminView(), DatosAcceso.Reset(error: "No se pudo resetear el Admin."),
            "acceso/reset-error.html", tamano: TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void BloqueoLicencia_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new BloqueoLicenciaView(), DatosAcceso.BloqueoLicencia(), "acceso/bloqueo-licencia.html");

    [AvaloniaFact]
    public void BloqueoLicencia_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new BloqueoLicenciaView(), DatosAcceso.BloqueoLicencia(), "acceso/bloqueo-licencia.html",
            tamano: TamanoLienzo.Municipio);
}
