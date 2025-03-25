using PaymentMS.Services;
using PaymentMS.Infra;
using Common.Events;
using Npgsql;
using System.Text.Json;
using Microsoft.Extensions.Options;

public class EventBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventBackgroundService> _logger;
    private readonly string _connectionString;

    public EventBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<EventBackgroundService> logger,
        IOptions<PaymentConfig> config
        )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _connectionString = config.Value.connectionString;
    }

    /// <summary>
    /// The main entry point for the BackgroundService.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Start a background listener for each channel
            StartNotificationThread("payment_invoice_issued_channel", stoppingToken);

            // Keep the service alive until the application stops
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Error in CartEventBackgroundService");
        }
    }

    /// <summary>
    /// Helper method that starts a Task for listening on a single channel.
    /// </summary>
    private void StartNotificationThread(string channelName, CancellationToken stoppingToken)
    {
        Task.Run(() => ListenForNotifications(_connectionString, channelName, stoppingToken), stoppingToken);
    }

    /// <summary>
    /// Continuously listens to PostgreSQL notifications on the given channel.
    /// </summary>
    private void ListenForNotifications(string connectionString, string channelName, CancellationToken cancellationToken)
    {
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();

            // Subscribe to the channel
            using (var cmd = new NpgsqlCommand($"LISTEN {channelName};", conn))
            {
                cmd.ExecuteNonQuery();
            }

            // When a notification comes in, handle it
            conn.Notification += async (sender, e) =>
            {
                _logger.LogInformation($"Received notification on {channelName}: Payload={e.Payload}");
                await HandleNotification(e.Channel, e.Payload);
            };

            // Continuously block until a notification arrives or cancellation is requested
            while (!cancellationToken.IsCancellationRequested)
            {
                conn.Wait(); 
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical($"Error in notification listener for channel {channelName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Dispatches notifications to the correct handler method based on channel.
    /// </summary>
    private async Task HandleNotification(string channel, string payload)
    {
        using var scope = _scopeFactory.CreateScope();

        var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();

        switch (channel)
        {
            case "payment_invoice_issued_channel":
                var invoiceIssued = ParseInvoiceIssued(payload);
                try
                {
                    await paymentService.ProcessPayment(invoiceIssued);
                }
                catch (Exception e)
                {
                    _logger.LogCritical(e.ToString());
                    // If normal processing fails, handle "poison" event
                    await paymentService.ProcessPoisonPayment(invoiceIssued);
                }
                break;

            default:
                _logger.LogWarning($"Unknown notification channel: {channel}");
                break;
        }
    }

    private InvoiceIssued ParseInvoiceIssued(string payload)
    {
        return JsonSerializer.Deserialize<InvoiceIssued>(payload) 
            ?? throw new InvalidOperationException("Deserialization returned null (ProductUpdated)");
    }


}
