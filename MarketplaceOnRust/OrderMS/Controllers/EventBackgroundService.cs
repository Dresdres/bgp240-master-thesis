using OrderMS.Infra;
using OrderMS.Services;
using Common.Events;
using Npgsql;
using System.Text.Json;
using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OrderMS.Common.Infra;



public class EventBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventBackgroundService> _logger;
    private readonly string _connectionString;

    public EventBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<EventBackgroundService> logger,
        IOptions<OrderConfig> config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _connectionString = config.Value.connectionString;
    }

    /// <summary>
    /// Main entry point for the BackgroundService; runs once at app startup.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Start a listening task for each channel you need
            StartNotificationThread("order_stock_confirmed_channel", stoppingToken);
            StartNotificationThread("order_shipment_channel", stoppingToken);

            // Keep this background service alive until the application stops
            while (!stoppingToken.IsCancellationRequested)
            {
                // You can do periodic checks/logging here if desired
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Error in OrderEventBackgroundService");
        }
    }

    /// <summary>
    /// Spins up a Task to listen on the specified PostgreSQL channel.
    /// </summary>
    private void StartNotificationThread(string channelName, CancellationToken stoppingToken)
    {
        Task.Run(() => ListenForNotifications(_connectionString, channelName, stoppingToken), stoppingToken);
    }

    /// <summary>
    /// Continuously listens for notifications on a single channel.
    /// </summary>
    private void ListenForNotifications(string connectionString, string channelName, CancellationToken cancellationToken)
    {
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();

            // Subscribe to the specific notification channel
            using (var cmd = new NpgsqlCommand($"LISTEN {channelName};", conn))
            {
                cmd.ExecuteNonQuery();
            }

            // Handle notifications
            conn.Notification += async (sender, e) =>
            {
                _logger.LogInformation($"Received notification on {channelName}: Payload={e.Payload}");
                await HandleNotification(e.Channel, e.Payload);
            };

            // Continuously wait for notifications until canceled
            while (!cancellationToken.IsCancellationRequested)
            {
                conn.Wait(); // Blocks until a notification is received or cancellation is triggered
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical($"Error in notification listener for channel {channelName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Dispatches notifications to the appropriate handlers based on channel.
    /// </summary>
    private async Task HandleNotification(string channel, string payload)
    {
        using var scope = _scopeFactory.CreateScope();
        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
        switch (channel)
        {
            // NOTE: The original code listened on "stock_failed_channel"
            // but the switch used "stock_confirmed_channel". Adjust as needed.

            case "order_stock_confirmed_channel": 
                var stockConfirmed = ParseStockConfirmedPayload(payload);
                try
                {
                    await orderService.ProcessStockConfirmed(stockConfirmed);
                }
                catch (Exception e)
                {
                    _logger.LogCritical(e.ToString());
                    await orderService.ProcessPoisonStockConfirmed(stockConfirmed);
                }
                break;

            // Example for "shipment_channel" if uncommented in the future:
            
            case "order_shipment_channel":
                var shipmentNotif = ParseShipmentPayload(payload);
                try
                {
                    orderService.ProcessShipmentNotification(shipmentNotif);
                }
                catch (Exception e)
                {
                    _logger.LogCritical(e.ToString());
                    // Decide what to do if shipment processing fails
                }
                break;
            

            default:
                _logger.LogWarning($"Unknown notification channel: {channel}");
                break;
        }
    }

    private StockConfirmed ParseStockConfirmedPayload(string payload)
    {
        return JsonSerializer.Deserialize<StockConfirmed>(payload)
            ?? throw new InvalidOperationException("Deserialization returned null for StockConfirmed");
    }

    private ShipmentNotification ParseShipmentPayload(string payload)
    {
        return JsonSerializer.Deserialize<ShipmentNotification>(payload)
            ?? throw new InvalidOperationException("Deserialization returned null for ShipmentNotification");
    }
}

