using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Administracion;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

/// <summary>
/// GUARDIÁN de la regla "todo comando de guardado/confirmación se bloquea con errores de
/// entrada" (bug de integridad 2026-10-01). Un campo en rojo (texto que el converter rechaza,
/// "20a5" en un NumericUpDown) NO llega al ViewModel, que conserva el valor ANTERIOR: un comando
/// de guardar que no se bloquea guarda en silencio ese valor viejo.
///
/// QUÉ ES un comando de guardado (no depende solo del nombre):
///   1. por NOMBRE: una propiedad ICommand pública cuyo nombre empieza con un verbo de
///      confirmación (<see cref="VerbosDeConfirmacion"/>), o
///   2. por la VISTA: el Command de todo botón <c>Classes="primary"</c> de una vista (la acción
///      principal de la pantalla), descubierto leyendo los .axaml reales.
/// Los que NO deben bloquearse (navegación, abrir un selector de archivo) se eximen en
/// <see cref="Eximidos"/> con su razón.
///
/// QUÉ COMPRUEBA (comportamiento, no texto): construye el ViewModel real (dependencias con Moq),
/// lo deja en un estado en el que el comando SÍ puede ejecutarse (<see cref="Arreglos"/>), y
/// verifica que al poner <c>HayErroresDeEntrada = true</c> el comando avisa el cambio
/// (CanExecuteChanged, sin eso el botón no se entera) y su CanExecute pasa a false; además, que
/// fue creado con <c>ViewModelBase.ComandoDeGuardado</c> (sin eso el botón no recibe el tooltip).
/// </summary>
public class ComandosDeGuardadoGuardianTests
{
    /// <summary>Verbos de confirmación: guardar, registrar, agregar algo tipeado, o filtrar con lo
    /// tipeado (un filtro con un año inválido no debe correr con el año anterior).</summary>
    private static readonly Regex VerbosDeConfirmacion = new(
        @"^(Guardar|Confirmar|Registrar|Agregar|Alta|Crear|Aplicar|Analizar|Buscar|Filtrar|Recargar|Probar)\w*Command$",
        RegexOptions.Compiled);

    /// <summary>Candidatos que NO son de guardado, cada uno con su razón.</summary>
    private static readonly Dictionary<(string Vm, string Comando), string> Eximidos = new()
    {
        [("LineaPoaFormViewModel", "AgregarAsignacionCommand")] =
            "agrega una fila vacía a la grilla; no persiste nada ni usa lo tipeado",
        [("AdjuntosPanelViewModel", "AgregarCommand")] =
            "abre el selector de archivos y sube el elegido; no usa ningún campo tipeado",
        [("AdjuntosDocumentoPanelViewModel", "AgregarCommand")] =
            "abre el selector de archivos y sube el elegido; no usa ningún campo tipeado",
        [("CalendarioPagosViewModel", "RegistrarPagoCommand")] =
            "acción por fila que navega a la pantalla de pagos de esa factura; ahí se confirma",
        [("IngresoPorFacturaViewModel", "FinalizarCommand")] =
            "navegación posterior al guardado (vuelve a Gastos); no confirma nada tipeado",
        [("NuevaImportacionViewModel", "NuevaImportacionCommand")] =
            "reinicia el asistente; descartar lo tipeado no debe bloquearse por un campo en rojo",
        [("ActualizacionBloqueoViewModel", "AplicarYReiniciarCommand")] =
            "aplica una actualización ya descargada; la pantalla no tiene ningún campo tipeado",
    };

    /// <summary>Botones primarios que son NAVEGACIÓN ("Nuevo ..." abre el formulario vacío): no
    /// confirman nada tipeado en la pantalla actual.</summary>
    private static readonly Regex PrimariosDeNavegacion = new(@"^Nuev[oa]Command$", RegexOptions.Compiled);

    /// <summary>
    /// Estado en el que el comando puede ejecutarse sin errores de entrada (sin esto, un comando
    /// que ya está en false por otra razón pasaría el guardián sin probar nada). Primero se corre
    /// <see cref="LlenarCampos"/> (completa lo obligatorio de cualquier formulario); esto es SOLO
    /// para lo que no se resuelve llenando propiedades públicas. Clave: nombre del ViewModel.
    /// </summary>
    private static readonly Dictionary<string, Action<object>> Arreglos = new()
    {
        // Guardar exige un usuario seleccionado, activo y no Admin en la pantalla padre.
        [nameof(PanelPermisosViewModel)] = vm =>
        {
            var panel = (PanelPermisosViewModel)vm;
            var padre = (UsuariosAdminViewModel)Construir(typeof(UsuariosAdminViewModel),
                new Dictionary<Type, object> { [typeof(PanelPermisosViewModel)] = panel });
            padre.UsuarioSeleccionado = new UsuarioDto(2, "prueba", null, RolUsuario.Operador, true, DateTime.Today);
            panel.MensajeError = null;
        },
        // Guardar exige al menos un renglón cargado.
        [nameof(IngresoPorFacturaViewModel)] = vm =>
            ((IngresoPorFacturaViewModel)vm).Renglones.Add(new FilaRenglonFacturaVm()),
        // Analizar exige las dos planillas elegidas (llegan por el selector de archivos).
        [nameof(NuevaImportacionViewModel)] = vm =>
        {
            foreach (var campo in new[] { "_gastosContenido", "_poaContenido" })
                typeof(NuevaImportacionViewModel).GetField(campo, BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(vm, new byte[] { 1 });
        },
    };

    /// <summary>
    /// Completa genéricamente un formulario como lo haría el usuario: texto vacío -> "1" (sirve
    /// para nombres, montos y años en texto), número en 0 -> 1, fecha nula -> hoy, selección nula
    /// -> una instancia del tipo. No toca los Mensaje* (un MensajeError no vacío bloquea), bools (OperacionEnCurso, GuardadoExitoso...) ni
    /// colecciones ni comandos, ni lo de ViewModelBase.
    /// </summary>
    private static void LlenarCampos(ViewModelBase vm)
    {
        var propiedades = vm.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.SetMethod!.IsPublic
                        && p.DeclaringType != typeof(ViewModelBase) && p.GetIndexParameters().Length == 0);
        foreach (var p in propiedades)
        {
            var tipo = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            var actual = p.GetValue(vm);
            object? valor = null;
            if (tipo == typeof(string) && string.IsNullOrEmpty((string?)actual)
                && !p.Name.StartsWith("Mensaje", StringComparison.Ordinal))
                valor = "1";
            else if ((tipo == typeof(int) || tipo == typeof(decimal)) && (actual is null || Convert.ToDecimal(actual) == 0))
                valor = Convert.ChangeType(1, tipo);
            else if (tipo == typeof(DateTime) && actual is null)
                valor = DateTime.Today;
            else if (tipo == typeof(DateTimeOffset) && actual is null)
                valor = new DateTimeOffset(DateTime.Today);
            else if (actual is null && tipo.IsClass && tipo != typeof(string)
                     && !typeof(System.Collections.IEnumerable).IsAssignableFrom(tipo)
                     && !typeof(ICommand).IsAssignableFrom(tipo) && !typeof(Delegate).IsAssignableFrom(tipo))
                valor = Construir(tipo, new Dictionary<Type, object>());

            if (valor is null)
                continue;
            try
            {
                p.SetValue(vm, valor);
            }
            catch (TargetInvocationException)
            {
                // Un setter que rechaza el valor genérico: lo resuelve un arreglo específico.
            }
        }
    }

    // ── descubrimiento ────────────────────────────────────────────────────────────────────

    private static string DirViews([CallerFilePath] string archivo = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(archivo)!, "..", "..", "..",
            "src", "StockApp.Presentation", "Views"));

    private static readonly Type[] TiposDeViewModel = typeof(ViewModelBase).Assembly.GetTypes()
        .Where(t => typeof(ViewModelBase).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition)
        .ToArray();

    private static readonly Regex DataTypeRaiz = new(@"x:DataType=""(?:\w+:)?(?<tipo>\w+)""", RegexOptions.Compiled);
    private static readonly Regex Boton = new(@"<Button\b[^>]*>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex EsPrimario = new(@"Classes=""[^""]*\bprimary\b|Classes\.primary=", RegexOptions.Compiled);
    private static readonly Regex CommandBinding = new(@"Command=""\{Binding (?<cmd>\w+)\}""", RegexOptions.Compiled);

    /// <summary>(tipo de ViewModel, comando) de cada botón primario de las vistas reales.</summary>
    public static IReadOnlyList<(Type Vm, string Comando)> BotonesPrimarios()
    {
        var resultado = new List<(Type, string)>();
        foreach (var archivo in Directory.GetFiles(DirViews(), "*.axaml", SearchOption.AllDirectories))
        {
            var texto = File.ReadAllText(archivo);
            var nombreVm = DataTypeRaiz.Match(texto) is { Success: true } m
                ? m.Groups["tipo"].Value
                : Path.GetFileNameWithoutExtension(archivo).Replace("View", "ViewModel");
            var tiposVm = typeof(ViewModelBase).Assembly.GetTypes()
                .Where(t => t.Name == nombreVm)
                .SelectMany(t => TiposDeViewModel.Where(t.IsAssignableFrom))
                .ToList();

            foreach (System.Text.RegularExpressions.Match b in Boton.Matches(texto))
            {
                if (!EsPrimario.IsMatch(b.Value) || CommandBinding.Match(b.Value) is not { Success: true } c)
                    continue;
                var comando = c.Groups["cmd"].Value;
                if (PrimariosDeNavegacion.IsMatch(comando))
                    continue;
                Assert.True(tiposVm.Count > 0,
                    $"{Path.GetFileName(archivo)}: botón primario {comando} sin ViewModel identificable ({nombreVm})");
                resultado.AddRange(tiposVm.Select(t => (t, comando)));
            }
        }
        return resultado;
    }

    private static IEnumerable<string> ComandosPorNombre(Type vm)
        => vm.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType) && VerbosDeConfirmacion.IsMatch(p.Name))
            .Select(p => p.Name);

    public static IReadOnlyList<(Type Vm, string Comando)> Candidatos()
        => TiposDeViewModel.SelectMany(t => ComandosPorNombre(t).Select(c => (t, c)))
            .Concat(BotonesPrimarios())
            .Distinct()
            .Where(x => !Eximidos.ContainsKey((x.Item1.Name, x.Item2)))
            .OrderBy(x => x.Item1.Name).ThenBy(x => x.Item2)
            .ToList();

    public static TheoryData<string, string> CasosDeGuardado()
    {
        var datos = new TheoryData<string, string>();
        foreach (var (vm, comando) in Candidatos())
            datos.Add(vm.Name, comando);
        return datos;
    }

    // ── el guardián ───────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(CasosDeGuardado))]
    public void ComandoDeGuardado_ConErroresDeEntrada_SeBloqueaYAvisa(string nombreVm, string nombreComando)
    {
        var tipo = TiposDeViewModel.Single(t => t.Name == nombreVm);
        var vm = (ViewModelBase)Construir(tipo, new Dictionary<Type, object>());
        LlenarCampos(vm);
        if (Arreglos.TryGetValue(nombreVm, out var arreglar))
            arreglar(vm);
        var propiedad = tipo.GetProperty(nombreComando, BindingFlags.Public | BindingFlags.Instance);
        Assert.True(propiedad is not null,
            $"{nombreVm} no expone {nombreComando}, pero un botón primario de su vista lo bindea");
        var comando = (ICommand)propiedad!.GetValue(vm)!;

        Assert.True(comando.CanExecute(null),
            $"{nombreVm}.{nombreComando}: no se pudo armar un estado en el que pueda ejecutarse; "
            + "agregá un arreglo en ComandosDeGuardadoGuardianTests.Arreglos (sin eso el guardián no prueba nada).");

        var avisos = 0;
        comando.CanExecuteChanged += (_, _) => avisos++;
        vm.HayErroresDeEntrada = true;

        Assert.True(avisos > 0,
            $"{nombreVm}.{nombreComando}: no avisa CanExecuteChanged al aparecer un error de entrada (el botón no se entera). "
            + "Crealo con ViewModelBase.ComandoDeGuardado(...).");
        Assert.False(comando.CanExecute(null),
            $"{nombreVm}.{nombreComando}: sigue habilitado con un campo en rojo y guardaría el valor VIEJO. "
            + "Crealo con ViewModelBase.ComandoDeGuardado(...).");
        Assert.True(vm.EsComandoDeGuardado(comando),
            $"{nombreVm}.{nombreComando}: se bloquea, pero no fue creado con ViewModelBase.ComandoDeGuardado(...) "
            + "y su botón no explica el bloqueo (tooltip).");

        vm.HayErroresDeEntrada = false;
        Assert.True(comando.CanExecute(null), $"{nombreVm}.{nombreComando}: no se rehabilita al corregir el campo.");
    }

    /// <summary>El guardián no puede quedar vacío en silencio (ej. si cambia la carpeta de vistas o
    /// el regex deja de matchear): fija candidatos conocidos de cada vía de identificación.</summary>
    [Fact]
    public void Descubrimiento_EncuentraCandidatosPorNombreYPorBotonPrimario()
    {
        var candidatos = Candidatos().Select(x => $"{x.Vm.Name}.{x.Comando}").ToList();
        var primarios = BotonesPrimarios().Select(x => $"{x.Vm.Name}.{x.Comando}").ToList();

        Assert.Contains("ProductoFormViewModel.GuardarCommand", candidatos);
        Assert.Contains("EntradaRegistroViewModel.RegistrarCommand", candidatos);
        Assert.Contains("IngresoPorFacturaViewModel.AgregarArticuloCommand", candidatos);
        Assert.Contains("NuevaImportacionViewModel.ConfirmarCommand", candidatos);
        // Solo por la vista (el nombre no es un verbo de confirmación):
        Assert.Contains("LoginViewModel.EntrarCommand", primarios);
        Assert.Contains("LoginViewModel.EntrarCommand", candidatos);
        Assert.DoesNotContain(primarios, p => p.EndsWith(".NuevoCommand", StringComparison.Ordinal));
    }

    /// <summary>Una exención que ya no corresponde a ningún comando real es basura que esconde.</summary>
    [Fact]
    public void CadaExencion_CorrespondeAUnComandoQueExiste()
    {
        foreach (var ((vm, comando), _) in Eximidos)
        {
            var tipo = TiposDeViewModel.SingleOrDefault(t => t.Name == vm);
            Assert.True(tipo?.GetProperty(comando) is not null, $"Exención obsoleta: {vm}.{comando}");
        }
    }

    // ── construcción genérica del ViewModel real ──────────────────────────────────────────

    private static object Construir(Type tipo, Dictionary<Type, object> cache)
    {
        if (cache.TryGetValue(tipo, out var ya))
            return ya;

        object instancia;
        if (tipo == typeof(ICurrentSession))
            instancia = SesionAdmin();
        else if (tipo == typeof(string))
            instancia = string.Empty;
        else if (tipo.IsValueType)
            instancia = Activator.CreateInstance(tipo)!;
        else if (typeof(Delegate).IsAssignableFrom(tipo))
            instancia = DelegadoNeutro(tipo);
        else if (tipo.IsInterface || tipo.IsAbstract)
        {
            var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(tipo))!;
            mock.DefaultValue = DefaultValue.Mock;
            instancia = mock.Object;
        }
        else
        {
            var ctor = tipo.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            var argumentos = ctor.GetParameters()
                .Select(p => p.HasDefaultValue && p.ParameterType.IsValueType ? p.DefaultValue : Construir(p.ParameterType, cache))
                .ToArray();
            instancia = ctor.Invoke(argumentos);
        }

        cache[tipo] = instancia;
        return instancia;
    }

    private static ICurrentSession SesionAdmin()
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.EstaAutenticado).Returns(true);
        sesion.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        sesion.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, "Admin"));
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        return sesion.Object;
    }

    private static Delegate DelegadoNeutro(Type tipo)
    {
        var invoke = tipo.GetMethod("Invoke")!;
        var parametros = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
        Expression cuerpo = invoke.ReturnType == typeof(void)
            ? Expression.Empty()
            : Expression.Default(invoke.ReturnType);
        return Expression.Lambda(tipo, cuerpo, parametros).Compile();
    }
}
