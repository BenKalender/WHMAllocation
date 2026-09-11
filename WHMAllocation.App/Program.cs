using Microsoft.EntityFrameworkCore;
using WHMAllocation.Infrastructure.Persistence;
using WHMAllocation.Infrastructure.Persistence.Seed;
using WHMAllocation.Infrastructure.Repositories;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Core.Interfaces.Services;
using WHMAllocation.Core.Services;
using WHMAllocation.Core.Interfaces;
using WHMAllocation.App.Components;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(
        "Data Source=../WHMAllocation.Infrastructure/Persistence/whmallocation.db");
});

builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<ISkuRepository, SkuRepository>();
builder.Services.AddScoped<IAllocationRepository, AllocationRepository>();
builder.Services.AddScoped<IAllocationService, AllocationService>();
builder.Services.AddScoped<IOrderCancellationService, OrderCancellationService>();
builder.Services.AddScoped<IInventoryCorrectionService, InventoryCorrectionService>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<DatabaseSeeder>();

var app = builder.Build();

// Development only: applying migrations automatically is convenient for the demo but is not
// something to do on a real deployment.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();

    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    var seeded = await seeder.SeedAsync();

    app.Logger.LogInformation(
        seeded ? "Demo data seeded." : "Existing data found, seeding skipped.");
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();