using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// AppDbContext: Scoped por request (patrón natural de ASP.NET Core). La app desktop
// sigue con AppDbContext Transient en su propia composición root — no se unifican.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Falta la cadena de conexión 'ConnectionStrings:Default' en appsettings.json. " +
        "Se requiere un PostgreSQL accesible (contenedor Docker local u on-premise).");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "StockApp.Api" }));

app.Run();

public partial class Program;
