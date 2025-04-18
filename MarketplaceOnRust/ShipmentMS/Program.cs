﻿using Microsoft.EntityFrameworkCore;
using ShipmentMS.Infra;
using ShipmentMS.Repositories;
using ShipmentMS.Repositories.Impl;
using ShipmentMS.Service;
using MysticMind.PostgresEmbed;
using Common.Utils;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions();

IConfigurationSection configSection = builder.Configuration.GetSection("ShipmentConfig");
builder.Services.Configure<ShipmentConfig>(configSection);
var config = configSection.Get<ShipmentConfig>();
if (config == null)
    Environment.Exit(1);

Task? waitPgSql = null;
if (config.PostgresEmbed)
{
    PgServer server;
    var instanceId = Utils.GetGuid("ShipmentDb");
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
    server = new PgServer("15.3.0", port: 5435, pgServerParams: serverParams, instanceId: instanceId);
    waitPgSql = server.StartAsync();
}

if (config.InMemoryDb)
{
    builder.Services.AddSingleton<IShipmentRepository, InMemoryShipmentRepository>();
    builder.Services.AddSingleton<IPackageRepository, InMemoryPackageRepository>();
} else {
    builder.Services.AddDbContext<ShipmentDbContext>();
    builder.Services.AddScoped<IShipmentRepository, ShipmentRepository>();
    builder.Services.AddScoped<IPackageRepository, PackageRepository>();
}

builder.Services.AddScoped<IShipmentService, ShipmentService>();

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
    }

    if(!config.InMemoryDb){
        var context = services.GetRequiredService<ShipmentDbContext>();
        context.Database.Migrate();

        if (!config.Logging)
        {
            var tableNames = context.Model.GetEntityTypes()
                                .Select(t => t.GetTableName())
                                .Distinct()
                                .ToList();
            foreach (var table in tableNames)
            {
                context.Database.ExecuteSqlRaw($"ALTER TABLE shipment.{table} SET unlogged");
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
