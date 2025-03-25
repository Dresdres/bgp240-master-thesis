using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;                         // for Postgres connections
using Common.Workload;                // for TransactionMark
using Common.Workload.Metrics;        // for TransactionOutput
using Common.Streaming;               // for Shared, MarkStatus, etc.

public class EventBackgroundService : BackgroundService
{
    private readonly ILogger<EventBackgroundService> _logger;
    private readonly string _connectionString;

    public EventBackgroundService(ILogger<EventBackgroundService> logger)
    {
        _logger = logger;
        _connectionString = "Host=andreas-postgres;Port=5432;Database=postgres;Username=ucloud;Password=ucloud;";
    }

    private static readonly JsonSerializerOptions s_enumOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter() // <--- Tells System.Text.Json to parse enums from string
        }
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Start a notification listener for each channel
            StartNotificationListener("productupdatemark", stoppingToken);
            StartNotificationListener("priceupdatemark", stoppingToken);
            StartNotificationListener("checkoutmark", stoppingToken);

            // Keep the background service alive until the application stops
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Error in EventBackgroundService");
        }
    }

    private void StartNotificationListener(string channelName, CancellationToken stoppingToken)
    {
        // Fire off a task that will block on NpgsqlConnection.Wait()
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

            // Hook up the event handler for when notifications arrive
            conn.Notification += async (sender, e) =>
            {
                _logger.LogInformation($"Received notification on {e.Channel}. Payload={e.Payload}");
                await HandleNotification(e.Channel, e.Payload);
            };

            // Block here waiting for notifications, until cancellation is requested
            while (!cancellationToken.IsCancellationRequested)
            {
                conn.Wait(); 
            }

            // Optionally, close the connection gracefully when canceled
            conn.Close();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, $"Error in notification listener for channel {channelName}");
        }
    }

    private async Task HandleNotification(string channel, string payload)
    {
        TransactionMark mark;
        try
        {
            mark = JsonSerializer.Deserialize<TransactionMark>(payload, s_enumOptions);
            if (mark == null)
                throw new InvalidOperationException("Deserialization returned null");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error deserializing payload on channel={channel}");
            return;
        }

        // Equivalent logic to your old Dapr-based EventHandler
        var ts = DateTime.UtcNow;
        await Shared.ResultQueue.Writer.WriteAsync(Shared.ITEM);

        switch (channel)
        {
            case "productupdatemark":
                if (mark.status == MarkStatus.SUCCESS)
                {
                    await Shared.ProductUpdateOutputs.Writer.WriteAsync(
                        new TransactionOutput(mark.tid, ts)
                    );
                }
                else
                {
                    await Shared.PoisonProductUpdateOutputs.Writer.WriteAsync(mark);
                }
                break;

            case "priceupdatemark":
                if (mark.status == MarkStatus.SUCCESS)
                {
                    await Shared.PriceUpdateOutputs.Writer.WriteAsync(
                        new TransactionOutput(mark.tid, ts)
                    );
                }
                else
                {
                    await Shared.PoisonPriceUpdateOutputs.Writer.WriteAsync(mark);
                }
                break;

            case "checkoutmark":
                if (mark.status == MarkStatus.SUCCESS)
                {
                    await Shared.CheckoutOutputs.Writer.WriteAsync(
                        new TransactionOutput(mark.tid, ts)
                    );
                }
                else
                {
                    await Shared.PoisonCheckoutOutputs.Writer.WriteAsync(mark);
                }
                break;

            default:
                _logger.LogWarning($"Unknown channel received: {channel}");
                break;
        }
    }
}
