using Npgsql;
using System.Text.Json;
using Common.Events;
using StockMS.Services;
using StockMS.Infra;
using Microsoft.Extensions.Options;


public class EventBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventBackgroundService> _logger;
    private readonly string _connectionString;

    public EventBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<EventBackgroundService> logger,
        IOptions<StockConfig> config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _connectionString = config.Value.connectionString;
    }

    /// <summary>
    /// The main entry point for the background service.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Start a listener for each channel you need:
            StartNotificationListener("stock_product_update_channel", stoppingToken);
            StartNotificationListener("stock_checkout_update_channel", stoppingToken);
            StartNotificationListener("stock_payment_confirmed_channel", stoppingToken);
            StartNotificationListener("stock_payment_failed_channel", stoppingToken);

            // Keep the background service alive until the application stops
            while (!stoppingToken.IsCancellationRequested)
            {
                // Optionally, add periodic checks/logging:
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Error in StockEventBackgroundService");
        }
    }

    private void StartNotificationListener(string channelName, CancellationToken stoppingToken)
    {
        Task.Run(() => ListenForNotifications(_connectionString, channelName, stoppingToken), stoppingToken);
    }

    private void ListenForNotifications(string connectionString, string channelName, CancellationToken cancellationToken)
    {
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();

            // Subscribe to the specified notification channel
            using (var cmd = new NpgsqlCommand($"LISTEN {channelName};", conn))
            {
                cmd.ExecuteNonQuery();
            }

            conn.Notification += async (sender, e) =>
            {
                _logger.LogInformation($"Received notification on {channelName}: Payload={e.Payload}");
                await HandleNotification(e.Channel, e.Payload);
            };

            // Continuously wait for notifications until cancellation is requested
            while (!cancellationToken.IsCancellationRequested)
            {
                conn.Wait(); // Blocks until a notification is received
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical($"Error in notification listener for channel {channelName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Handle the incoming notification based on the channel name.
    /// </summary>
    private async Task HandleNotification(string channel, string payload)
    {
        using var scope = _scopeFactory.CreateScope();
        var stockService = scope.ServiceProvider.GetRequiredService<IStockService>();
        switch (channel)
        {
            case "stock_product_update_channel":
                try
                {
                    var productUpdated = ParseProductUpdatePayload(payload);
                    await stockService.ProcessProductUpdate(productUpdated);
                }
                catch (Exception e)
                {
                    _logger.LogCritical(e.ToString());
                    var productUpdated = ParseProductUpdatePayload(payload);
                    await stockService.ProcessPoisonProductUpdate(productUpdated);
                }
                break;

            case "stock_checkout_update_channel":
                try
                {
                    var checkoutUpdated = ParseCheckoutUpdatePayload(payload);
                    await stockService.ReserveStockAsync(checkoutUpdated);
                }
                catch (Exception e)
                {
                    _logger.LogCritical(e.ToString());
                    var checkoutUpdated = ParseCheckoutUpdatePayload(payload);
                    await stockService.ProcessPoisonReserveStock(checkoutUpdated);
                }
                break;

            case "stock_payment_confirmed_channel":
                try
                {
                    var payment = ParsePaymentPayload(payload);
                    stockService.ConfirmReservation(payment);
                }
                catch (Exception e)
                {
                    _logger.LogCritical(e.ToString());
                }
                break;

            case "stock_payment_failed_channel":
                try
                {
                    var paymentFailed = ParsePaymentFailedPayload(payload);
                    stockService.CancelReservation(paymentFailed);
                }
                catch (Exception e)
                {
                    _logger.LogCritical(e.ToString());
                }
                break;

            default:
                _logger.LogWarning($"Unknown notification channel: {channel}");
                break;
        }

        // Return after processing
        return;
    }

    private ProductUpdated ParseProductUpdatePayload(string payload)
    {
        return JsonSerializer.Deserialize<ProductUpdated>(payload)
                ?? throw new InvalidOperationException("Deserialization returned null (ProductUpdated)");
    }

    private ReserveStock ParseCheckoutUpdatePayload(string payload)
    {
        return JsonSerializer.Deserialize<ReserveStock>(payload)
                ?? throw new InvalidOperationException("Deserialization returned null (ReserveStock)");
    }

    private PaymentConfirmed ParsePaymentPayload(string payload)
    {
        return JsonSerializer.Deserialize<PaymentConfirmed>(payload)
                ?? throw new InvalidOperationException("Deserialization returned null (PaymentConfirmed)");
    }

    private PaymentFailed ParsePaymentFailedPayload(string payload)
    {
        return JsonSerializer.Deserialize<PaymentFailed>(payload)
                ?? throw new InvalidOperationException("Deserialization returned null (PaymentFailed)");
    }
}
