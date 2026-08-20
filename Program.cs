using FortiScope.Services;
using FortiScope.Configuration;
using FortiScope.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataDirectory);
var databasePath = Path.Combine(dataDirectory, "fortiscope.db");

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.Configure<SnmpOptions>(builder.Configuration.GetSection(SnmpOptions.SectionName));
builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection(MonitoringOptions.SectionName));
builder.Services.AddDbContextFactory<FortiScopeDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddSingleton<ISnmpMonitoringService, SnmpMonitoringService>();
builder.Services.AddHostedService<SnmpMonitoringBackgroundService>();
builder.Services.AddHostedService<MetricPersistenceBackgroundService>();
builder.Services.AddSingleton<HistoryService>();

var app = builder.Build();

try
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<FortiScopeDbContext>();
    await dbContext.Database.MigrateAsync();
}
catch (Exception exception)
{
    app.Logger.LogError("SQLite migration uygulanamadı ({ExceptionType}). Uygulama SNMP izlemeye devam edecek.",
        exception.GetType().Name);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapGet("/api/monitoring/current", (ISnmpMonitoringService service) => Results.Ok(service.GetCurrent()));
app.MapGet("/api/history/system", async (string? range, HistoryService service, CancellationToken cancellationToken) =>
{
    if (!HistoryRangeParser.TryParse(range, out var duration))
        return Results.BadRequest(new { error = "Geçersiz range. Desteklenen değerler: 5m, 1h, 6h, 24h, 7d, 30d." });
    return Results.Ok(await service.GetSystemHistoryAsync(duration, cancellationToken));
});
app.MapGet("/api/history/interfaces/{interfaceIndex:int}", async (int interfaceIndex, string? range,
    HistoryService service, CancellationToken cancellationToken) =>
{
    if (interfaceIndex < 1) return Results.BadRequest(new { error = "Interface index pozitif olmalıdır." });
    if (!HistoryRangeParser.TryParse(range, out var duration))
        return Results.BadRequest(new { error = "Geçersiz range. Desteklenen değerler: 5m, 1h, 6h, 24h, 7d, 30d." });
    return Results.Ok(await service.GetInterfaceHistoryAsync(interfaceIndex, duration, cancellationToken));
});

app.Run();
