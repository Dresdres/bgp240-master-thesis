﻿using StockMS.Repositories;
using StockMS.Infra;
using StockMS.Services;
using Microsoft.EntityFrameworkCore;
using MysticMind.PostgresEmbed;
using Common.Utils;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions();

IConfigurationSection configSection = builder.Configuration.GetSection("StockConfig");
builder.Services.Configure<StockConfig>(configSection);
var config = configSection.Get<StockConfig>();
if (config == null)
    Environment.Exit(1);

Task? waitPgSql = null;
PgServer? server = null;
if (config.PostgresEmbed)
{
    var instanceId = Utils.GetGuid("StockDb");
    var serverParams = new Dictionary<string, string>
    {
        { "synchronous_commit", "off" },
        { "max_connections", "10000" },
        { "listen_addresses", "*" },
        { "shared_buffers", "3GB" },
        { "work_mem", "128MB" }
    };
    
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        serverParams.Add( "unix_socket_directories", "/tmp");
        serverParams.Add( "unix_socket_group", "" );
        serverParams.Add( "unix_socket_permissions", "0777");
    }
    server = new PgServer("15.3.0", port: 5433, pgServerParams: serverParams, instanceId: instanceId);
    waitPgSql = server.StartAsync();
}

if (config.InMemoryDb)
{
    builder.Services.AddSingleton<IStockRepository, InMemoryStockRepository>();
} else {
    builder.Services.AddDbContext<StockDbContext>();
    builder.Services.AddScoped<IStockRepository, StockRepository>();
}

builder.Services.AddScoped<IStockService, StockService>();

builder.Services.AddHostedService<EventBackgroundService>();

builder.Services.AddControllers();

builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    if (config.PostgresEmbed)
    {
        if (waitPgSql is not null) await waitPgSql;
        else
            throw new Exception("PostgreSQL was not setup correctly!");

        app.Lifetime.ApplicationStopped.Register(()=> {
            Console.WriteLine("Shutting down PostgreSQL embed");
            server?.Stop();
        });
    }

    if(!config.InMemoryDb){
        var context = services.GetRequiredService<StockDbContext>();
        context.Database.Migrate();

        if (!config.Logging)
        {
            var tableNames = context.Model.GetEntityTypes()
                                .Select(t => t.GetTableName())
                                .Distinct()
                                .ToList();
            foreach (var table in tableNames)
            {
                context.Database.ExecuteSqlRaw($"ALTER TABLE stock.{table} SET unlogged");
            }
    }
    }
}

if (config.Streaming)
    app.UseCloudEvents();

app.MapControllers();

app.MapHealthChecks("/health");

if (config.Streaming)
    app.MapSubscribeHandler();

app.Run();
