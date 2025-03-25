using Npgsql;
using System.Text.Json;
using Common.Events;
using SellerMS.Services;
using SellerMS.Infra;
using Microsoft.Extensions.Options;

public class EventBackgroundService : BackgroundService
{
    private readonly ILogger<EventBackgroundService> _logger;
    private readonly string _connectionString;
    private readonly IServiceScopeFactory _scopeFactory;

    public EventBackgroundService(
        ILogger<EventBackgroundService> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<SellerConfig> config)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _connectionString = config.Value.connectionString;
    }

    /// <summary>
    /// Main entry point for the BackgroundService; runs once at app startup.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Start a listening task for each channel
            StartNotificationThread("seller_invoice_issued_channel", stoppingToken);
            StartNotificationThread("seller_shipment_channel", stoppingToken);
            StartNotificationThread("seller_delivery_channel", stoppingToken);
            StartNotificationThread("seller_payment_failed_channel", stoppingToken);

            // Keep this service alive until application stops
            while (!stoppingToken.IsCancellationRequested)
            {
                // You can do periodic checks/logging here if desired
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Error in SellerEventBackgroundService");
        }

        return;
    }

    /// <summary>
    /// Helper that spins up a Task to listen on a given channel.
    /// </summary>
    private void StartNotificationThread(string channelName, CancellationToken stoppingToken)
    {
        Task.Run(() => ListenForNotifications(_connectionString, channelName, stoppingToken), stoppingToken);
    }

    /// <summary>
    /// Continuously listens for notifications on the specified channel.
    /// </summary>
    private void ListenForNotifications(string connectionString, string channelName, CancellationToken cancellationToken)
    {
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();

            // Subscribe to this channel
            using (var cmd = new NpgsqlCommand($"LISTEN {channelName};", conn))
            {
                cmd.ExecuteNonQuery();
            }

            // Handle notifications
            conn.Notification += (sender, e) =>
            {
                _logger.LogInformation($"Received notification on {channelName}: Payload={e.Payload}");
                HandleNotification(e.Channel, e.Payload);
            };

            // Continuously wait until cancellation is requested
            while (!cancellationToken.IsCancellationRequested)
            {
                conn.Wait(); // Blocks until a notification arrives
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical($"Error in notification listener for channel {channelName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Logic to handle a single notification, dispatching to the correct method by channel.
    /// </summary>
    private void HandleNotification(string channel, string payload)
    {
        using var scope = _scopeFactory.CreateScope();
        var sellerService = scope.ServiceProvider.GetRequiredService<ISellerService>();
        try
        {
            switch (channel)
            {
                case "seller_invoice_issued_channel":
                    var invoiceIssued = ParseInvoicePayload(payload);
                    sellerService.ProcessInvoiceIssued(invoiceIssued);
                    break;

                case "seller_shipment_channel":
                    var shipmentNotification = ParseShipmentPayload(payload);
                    sellerService.ProcessShipmentNotification(shipmentNotification);
                    break;

                case "seller_delivery_channel":
                    var deliveryNotification = ParseDeliveryPayload(payload);
                    sellerService.ProcessDeliveryNotification(deliveryNotification);
                    break;

                case "seller_payment_failed_channel":
                    var paymentFailed = ParsePaymentFailedPayload(payload);
                    sellerService.ProcessPaymentFailed(paymentFailed);
                    break;

                default:
                    _logger.LogWarning($"Unknown notification channel: {channel}");
                    break;
            }
        }
        catch (Exception ex)
        {
            // If an exception happens during processing, we log it
            _logger.LogCritical(ex.ToString());
        }

        return; // End method
    }

    private InvoiceIssued ParseInvoicePayload(string payload)
    {
        return JsonSerializer.Deserialize<InvoiceIssued>(payload)
            ?? throw new InvalidOperationException("Deserialization returned null (InvoiceIssued)");
    }

    private ShipmentNotification ParseShipmentPayload(string payload)
    {
        return JsonSerializer.Deserialize<ShipmentNotification>(payload)
            ?? throw new InvalidOperationException("Deserialization returned null (ShipmentNotification)");
    }

    private DeliveryNotification ParseDeliveryPayload(string payload)
    {
        return JsonSerializer.Deserialize<DeliveryNotification>(payload)
            ?? throw new InvalidOperationException("Deserialization returned null (DeliveryNotification)");
    }

    private PaymentFailed ParsePaymentFailedPayload(string payload)
    {
        return JsonSerializer.Deserialize<PaymentFailed>(payload)
            ?? throw new InvalidOperationException("Deserialization returned null (PaymentFailed)");
    }
}
