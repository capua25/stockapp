using StockApp.Application.Actualizaciones;

namespace StockApp.Application.Tests.Actualizaciones;

public class FormateadorNotasActualizacionTests
{
    // Contenido real de build/RELEASE_NOTES.md: severity + título + bullets + separador
    // "---" + bloque de instrucciones HTML internas para el publicador.
    private const string NotasReales =
        "severity: normal\n" +
        "\n" +
        "# StockApp 0.1.2\n" +
        "\n" +
        "- Nueva pantalla de bienvenida al iniciar sesión, con accesos rápidos.\n" +
        "- La tecla Enter ahora confirma los formularios de inicio de sesión.\n" +
        "- El número de versión de la app se muestra en pantalla (login y menú).\n" +
        "- Mejoras de estabilidad en el actualizador.\n" +
        "- Actualización de seguridad en el motor de base de datos.\n" +
        "\n" +
        "---\n" +
        "<!-- INSTRUCCIONES PARA EL PUBLICADOR — leer antes de empaquetar:\n" +
        "\n" +
        "LINEA DE SEVERITY (OBLIGATORIA — DEBE SER LA PRIMERA LINEA DEL ARCHIVO):\n" +
        "  severity: normal | important | critical\n" +
        "\n" +
        "FLUJO DE USO:\n" +
        "  1. Editar <Version> en src/StockApp.Presentation/StockApp.Presentation.csproj\n" +
        "\n" +
        "Ver build/README-empaquetado.md para el flujo completo.\n" +
        "-->\n";

    [Fact]
    public void Limpiar_NotasReales_NoContieneSeverity()
    {
        var resultado = FormateadorNotasActualizacion.Limpiar(NotasReales);

        Assert.DoesNotContain("severity:", resultado, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Limpiar_NotasReales_NoContieneInstruccionesDelPublicador()
    {
        var resultado = FormateadorNotasActualizacion.Limpiar(NotasReales);

        Assert.DoesNotContain("INSTRUCCIONES PARA EL PUBLICADOR", resultado);
    }

    [Fact]
    public void Limpiar_NotasReales_NoContieneFlujoDeUso()
    {
        var resultado = FormateadorNotasActualizacion.Limpiar(NotasReales);

        Assert.DoesNotContain("FLUJO DE USO", resultado);
    }

    [Fact]
    public void Limpiar_NotasReales_ContieneElTitulo()
    {
        var resultado = FormateadorNotasActualizacion.Limpiar(NotasReales);

        Assert.Contains("StockApp 0.1.2", resultado);
    }

    [Fact]
    public void Limpiar_NotasReales_ContieneLosBulletsLimpios()
    {
        var resultado = FormateadorNotasActualizacion.Limpiar(NotasReales);

        Assert.Contains("• Nueva pantalla de bienvenida al iniciar sesión, con accesos rápidos.", resultado);
        Assert.Contains("• Mejoras de estabilidad en el actualizador.", resultado);
    }

    [Fact]
    public void Limpiar_Null_DevuelveVacio()
    {
        Assert.Equal(string.Empty, FormateadorNotasActualizacion.Limpiar(null));
    }

    [Fact]
    public void Limpiar_StringVacio_DevuelveVacio()
    {
        Assert.Equal(string.Empty, FormateadorNotasActualizacion.Limpiar(string.Empty));
    }

    [Fact]
    public void Limpiar_SinSeparador_NoRompeYConservaContenido()
    {
        const string notas = "# Título\n\n- Un cambio.\n- Otro cambio.";

        var resultado = FormateadorNotasActualizacion.Limpiar(notas);

        Assert.Contains("Título", resultado);
        Assert.Contains("• Un cambio.", resultado);
        Assert.Contains("• Otro cambio.", resultado);
    }

    [Fact]
    public void Limpiar_SinSeverity_NoAlteraLaPrimeraLinea()
    {
        const string notas = "# Sin severity\n\n- Cambio único.";

        var resultado = FormateadorNotasActualizacion.Limpiar(notas);

        Assert.StartsWith("Sin severity", resultado);
    }
}
