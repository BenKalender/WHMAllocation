using Microsoft.EntityFrameworkCore;
using WHMAllocation.App.Components;
using WHMAllocation.Infrastructure.Persistence;
using WHMAllocation.Infrastructure.Repositories;
using WHMAllocation.Core.Interfaces.Repositories;
using WHMAllocation.Core.Interfaces.Services;
using WHMAllocation.Core.Services;
using WHMAllocation.Core.Interfaces;

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

var app = builder.Build();

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();