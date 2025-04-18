﻿using Microsoft.EntityFrameworkCore;
using PaymentMS.Infra;
using PaymentMS.Repositories;
using PaymentMS.Services;
using MysticMind.PostgresEmbed;
using Common.Utils;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions();

IConfigurationSection configSection = builder.Configuration.GetSection("PaymentConfig");
builder.Services.Configure<PaymentConfig>(configSection);
var config = configSection.Get<PaymentConfig>();
if (config == null)
    Environment.Exit(1);

Task? waitPgSql = null;
if (config.PostgresEmbed)
{
    PgServer server;
    var instanceId = Utils.GetGuid("PaymentDb");
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
    server = new PgServer("15.3.0", port: 5434, pgServerParams: serverParams, instanceId: instanceId);
    waitPgSql = server.StartAsync();
}

builder.Services.AddSingleton<HttpClient>();

if (config.InMemoryDb)
{
    builder.Services.AddSingleton<IPaymentRepository, InMemoryPaymentRepository>();
} else
{
    builder.Services.AddDbContext<PaymentDbContext>();
    builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
}

builder.Services.AddScoped<IExternalProvider, ExternalProviderProxy>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

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
        var context = services.GetRequiredService<PaymentDbContext>();
        context.Database.Migrate();

        if (!config.Logging)
        {
            var tableNames = context.Model.GetEntityTypes()
                                .Select(t => t.GetTableName())
                                .Distinct()
                                .ToList();
            foreach (var table in tableNames)
            {
                context.Database.ExecuteSqlRaw($"ALTER TABLE payment.{table} SET unlogged");
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
