using Microsoft.Extensions.DependencyInjection;
using StockApp.ApiClient;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.DI;

/// <summary>
/// Red de seguridad del cableado DI API-only (Fase 3b). Espejo de ConfigurarServicios en
/// App.axaml.cs — reemplaza a los 3 ComposicionDI* de las Fases 4-6, que armaban la cadena
/// vieja con AppDbContext/repos de Infrastructure. No hace ninguna llamada HTTP real:
/// solo verifica que el contenedor resuelve toda la cadena sin lanzar.
/// </summary>
public class ComposicionDIApiTests
{
    // Scope parcial deliberado (review final 3b): este espejo no incluye Shell/Inicio/el
    // bloque Updater (CoordinadorActualizacion + IUpdateService + IUiDispatcher + IInfoApp)
    // ni los 4 VMs de reportes (StockCategoriaViewModel, HistorialPorProductoViewModel,
    // MasMovidosViewModel, AuditoriaLogViewModel). Es drift preexistente heredado de los tests
    // viejos que este archivo reemplazó, no un gap nuevo introducido en 3b. Sus constructores
    // se siguen validando indirectamente: se resuelven en el arranque real de la app
    // (App.axaml.cs) y están cubiertos por sus propios tests de ViewModel unitarios.
    private static IServiceProvider CrearContenedor()
    {
        var services = new ServiceCollection();

        // ── Sesión API + HttpClient (espejo de App.axaml.cs, Fase 3b) ─────────
        services.AddSingleton<ApiSession>();
        services.AddSingleton<ICurrentSession>(sp => sp.GetRequiredService<ApiSession>());
        services.AddSingleton(sp =>
        {
            var handler = new AuthTokenHandler(sp.GetRequiredService<ApiSession>())
            {
                InnerHandler = new SocketsHttpHandler(),
            };
            return new HttpClient(handler)
            {
                BaseAddress = new Uri("http://localhost:5000/"),
                Timeout = TimeSpan.FromSeconds(10),
            };
        });

        // ── ApiClients: las mismas 9 interfaces de Application ────────────────
        services.AddTransient<IAuthService, AuthApiClient>();
        services.AddTransient<IUsuarioService, UsuarioApiClient>();
        services.AddTransient<IProductoService, ProductoApiClient>();
        services.AddTransient<ICategoriaService, CategoriaApiClient>();
        services.AddTransient<IProveedorService, ProveedorApiClient>();
        services.AddTransient<IUnidadMedidaService, UnidadMedidaApiClient>();
        services.AddTransient<IMovimientoStockService, MovimientoStockApiClient>();
        services.AddTransient<IReporteStockService, ReporteStockApiClient>();
        services.AddTransient<IAuditoriaQueryService, AuditoriaQueryApiClient>();

        // ── Servicios de Presentation (igual que App.axaml.cs) ────────────────
        services.AddSingleton<INavigationService>(sp =>
            new NavigationService(t => sp.GetRequiredService(t)));
        services.AddSingleton<IInfoApp, InfoApp>();
        services.AddSingleton<IConfirmacionService, ConfirmacionService>();
        services.AddSingleton<IServicioGuardadoArchivo, ServicioGuardadoArchivo>();
        services.AddTransient<ICsvExporter, CsvExporter>();

        // ── ViewModels (los mismos que cubrían los 3 tests reemplazados) ──────
        services.AddTransient<ShellMainViewModel>();
        services.AddTransient<ProductoListViewModel>();
        services.AddTransient<ProductoFormViewModel>();
        services.AddTransient<CategoriaListViewModel>();
        services.AddTransient<CategoriaFormViewModel>();
        services.AddTransient<ProveedorListViewModel>();
        services.AddTransient<ProveedorFormViewModel>();
        services.AddTransient<UnidadMedidaListViewModel>();
        services.AddTransient<UnidadMedidaFormViewModel>();
        services.AddTransient<EntradaRegistroViewModel>();
        services.AddTransient<SalidaRegistroViewModel>();
        services.AddTransient<MovimientoHistorialViewModel>();
        services.AddTransient<ValorizacionViewModel>();

        return services.BuildServiceProvider();
    }

    // ─── ApiClients: interfaz → implementación exacta ─────────────────────────

    [Theory]
    [InlineData(typeof(IAuthService), typeof(AuthApiClient))]
    [InlineData(typeof(IUsuarioService), typeof(UsuarioApiClient))]
    [InlineData(typeof(IProductoService), typeof(ProductoApiClient))]
    [InlineData(typeof(ICategoriaService), typeof(CategoriaApiClient))]
    [InlineData(typeof(IProveedorService), typeof(ProveedorApiClient))]
    [InlineData(typeof(IUnidadMedidaService), typeof(UnidadMedidaApiClient))]
    [InlineData(typeof(IMovimientoStockService), typeof(MovimientoStockApiClient))]
    [InlineData(typeof(IReporteStockService), typeof(ReporteStockApiClient))]
    [InlineData(typeof(IAuditoriaQueryService), typeof(AuditoriaQueryApiClient))]
    public void Contenedor_Resuelve_CadaInterfazConSuApiClient(Type interfaz, Type implementacion)
    {
        var sp = CrearContenedor();

        var servicio = sp.GetRequiredService(interfaz);

        Assert.IsType(implementacion, servicio);
    }

    // ─── Sesión y HttpClient ──────────────────────────────────────────────────

    [Fact]
    public void ICurrentSession_Y_ApiSession_SonLaMismaInstanciaSingleton()
    {
        var sp = CrearContenedor();

        var comoInterfaz = sp.GetRequiredService<ICurrentSession>();
        var comoConcreta = sp.GetRequiredService<ApiSession>();

        Assert.Same(comoConcreta, comoInterfaz);
    }

    [Fact]
    public void HttpClient_EsSingletonConBaseAddressTerminadaEnBarra()
    {
        var sp = CrearContenedor();

        var http1 = sp.GetRequiredService<HttpClient>();
        var http2 = sp.GetRequiredService<HttpClient>();

        Assert.Same(http1, http2);
        Assert.EndsWith("/", http1.BaseAddress!.ToString());
    }

    // ─── ViewModels: toda la cadena resuelve sin Infrastructure ───────────────

    [Theory]
    [InlineData(typeof(ShellMainViewModel))]
    [InlineData(typeof(ProductoListViewModel))]
    [InlineData(typeof(ProductoFormViewModel))]
    [InlineData(typeof(CategoriaListViewModel))]
    [InlineData(typeof(CategoriaFormViewModel))]
    [InlineData(typeof(ProveedorListViewModel))]
    [InlineData(typeof(ProveedorFormViewModel))]
    [InlineData(typeof(UnidadMedidaListViewModel))]
    [InlineData(typeof(UnidadMedidaFormViewModel))]
    [InlineData(typeof(EntradaRegistroViewModel))]
    [InlineData(typeof(SalidaRegistroViewModel))]
    [InlineData(typeof(MovimientoHistorialViewModel))]
    [InlineData(typeof(ValorizacionViewModel))]
    public void Contenedor_Resuelve_CadaViewModel(Type tipoVm)
    {
        var sp = CrearContenedor();

        var vm = sp.GetRequiredService(tipoVm);

        Assert.NotNull(vm);
    }

    [Fact]
    public void Contenedor_Resuelve_INavigationService_Y_IServicioGuardadoArchivo()
    {
        var sp = CrearContenedor();

        Assert.NotNull(sp.GetRequiredService<INavigationService>());
        Assert.NotNull(sp.GetRequiredService<IServicioGuardadoArchivo>());
    }
}
