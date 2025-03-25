using CartMS.Services;
using CartMS.Infra;
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
        IOptions<CartConfig> config
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
            StartNotificationThread("cart_price_update_channel", stoppingToken);
            StartNotificationThread("cart_product_update_channel", stoppingToken);

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
        // Because you need a scoped service (ICartService, etc.), create a scope here:
        using var scope = _scopeFactory.CreateScope();

        // Now you can safely resolve scoped services within this scope:
        var cartService = scope.ServiceProvider.GetRequiredService<ICartService>();
        // If you need the repositories, do the same:
        // var cartRepository = scope.ServiceProvider.GetRequiredService<ICartRepository>();
        // var productReplicaRepository = scope.ServiceProvider.GetRequiredService<IProductReplicaRepository>();
        switch (channel)
        {
            
            case "cart_product_update_channel":
                try
                {
                    ProductUpdated productUpdated = ParseProductUpdatePayload(payload);
                    cartService.ProcessProductUpdated(productUpdated);
                }
                catch (Exception e)
                {
                    _logger.LogCritical(e.ToString());
                    // If normal processing fails, handle "poison" event
                    ProductUpdated productUpdated = ParseProductUpdatePayload(payload);
                    await cartService.ProcessPoisonProductUpdated(productUpdated);
                }
                break;

            case "cart_price_update_channel":
                try
                {
                    var priceUpdated = ParsePriceUpdatePayload(payload);
                    await cartService.ProcessPriceUpdate(priceUpdated);
                }
                catch (Exception e)
                {
                    _logger.LogCritical(e.ToString());
                    // If normal processing fails, handle "poison" event
                    var priceUpdated = ParsePriceUpdatePayload(payload);
                    await cartService.ProcessPoisonPriceUpdate(priceUpdated);
                }
                break;

            default:
                _logger.LogWarning($"Unknown notification channel: {channel}");
                break;
        }
    }

    private ProductUpdated ParseProductUpdatePayload(string payload)
    {
        return JsonSerializer.Deserialize<ProductUpdated>(payload) 
            ?? throw new InvalidOperationException("Deserialization returned null (ProductUpdated)");
    }

    private PriceUpdated ParsePriceUpdatePayload(string payload)
    {
        return JsonSerializer.Deserialize<PriceUpdated>(payload) 
            ?? throw new InvalidOperationException("Deserialization returned null (PriceUpdated)");
    }
}
