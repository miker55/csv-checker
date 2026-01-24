using CsvChecker.Data;
using CsvChecker.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// DB: use factory so telemetry writes don't hold the Blazor circuit thread
var telemetryDbPath = PathProvider.GetTelemetryDbPath();
builder.Services.AddDbContextFactory<TelemetryDbContext>(opt =>
    opt.UseSqlite($"Data Source={telemetryDbPath}"));

// Services
builder.Services.AddSingleton<ReportStore>();
builder.Services.AddSingleton<CsvAnalyzer>();
builder.Services.AddSingleton<TelemetryService>();

var app = builder.Build();

// Optional: auto-apply migrations on startup (safe for v1 single instance)
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Download errors.csv
app.MapGet("/download/errors/{token}", (string token, ReportStore store) =>
{
    if (!store.TryGet(token, out var result) || result is null)
        return Results.NotFound();

    var bytes = ErrorsCsvWriter.ToCsvBytes(result);
    var downloadName = "errors.csv";
    return Results.File(bytes, "text/csv; charset=utf-8", downloadName);
});

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

await app.RunAsync();
