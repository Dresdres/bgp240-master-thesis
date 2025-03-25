using Common.Events;
using CustomerMS.Services;
using CustomerMS.Infra;
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
        IOptions<CustomerConfig> config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _connectionString = config.Value.connectionString;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Start background tasks here:
            StartNotificationThread("customer_stock_failed_channel", stoppingToken);
            StartNotificationThread("customer_payment_confirmed_channel", stoppingToken);
            StartNotificationThread("customer_payment_failed_channel", stoppingToken);
            StartNotificationThread("customer_delivery_channel", stoppingToken);

            // Keep this background service alive until the application stops
            while (!stoppingToken.IsCancellationRequested)
            {
                // Just delay in a loop, or do any periodic checks if needed
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Error in background listener service");
        }
    }

    private void StartNotificationThread(string channelName, CancellationToken stoppingToken)
    {
        Task.Run(() => ListenForNotifications(_connectionString, channelName, stoppingToken), stoppingToken);
    }

    private void ListenForNotifications(string connectionString, string channelName, CancellationToken cancellationToken)
    {
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();

            // Subscribe
            using (var cmd = new NpgsqlCommand($"LISTEN {channelName};", conn))
            {
                cmd.ExecuteNonQuery();
            }

            conn.Notification += (sender, e) =>
            {
                _logger.LogInformation($"Received notification on {channelName}: Payload={e.Payload}");
                HandleNotification(e.Channel, e.Payload);
            };

            // Continuously wait for notifications until cancellation is requested
            while (!cancellationToken.IsCancellationRequested)
            {
                conn.Wait(); // Will block until a notification arrives
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical($"Error in notification listener for channel {channelName}: {ex.Message}");
        }
    }

        private void HandleNotification(string channel, string payload)
        {
            using var scope = _scopeFactory.CreateScope();
            var customerService = scope.ServiceProvider.GetRequiredService<ICustomerService>();
            switch (channel)
            {
                case "customer_stock_failed_channel":
                    try
                    {
                        var reserveStockFailed = ParseProductUpdatePayload(payload); // Deserialize payload to ProductUpdated object
                        this._logger.LogInformation("[ProcessFailedReservation] received for customer {0}", reserveStockFailed.customerCheckout.CustomerId);
                        this._logger.LogInformation("[ProcessFailedReservation] completed for customer {0}.", reserveStockFailed.customerCheckout.CustomerId);
                        break;
                    }
                    catch (Exception e)
                    {
                        this._logger.LogCritical(e.ToString());
                        this._logger.LogInformation("Failed to process notification: {0}", e.Message);
                    }
                    break;
                case "customer_payment_confirmed_channel":
                    try
                    {
                        var paymentConfirmed = ParsePaymentPayload(payload); // Deserialize payload to ProductUpdated object
                        this._logger.LogInformation("[ProcessPaymentConfirmed] received for customer {0}", paymentConfirmed.customer.CustomerId);
                        customerService.ProcessPaymentConfirmed(paymentConfirmed);
                        this._logger.LogInformation("[ProcessPaymentConfirmed] completed for customer {0}.", paymentConfirmed.customer.CustomerId);
                        break;
                    }
                    catch (Exception e)
                    {
                        this._logger.LogCritical(e.ToString());
                        this._logger.LogInformation("Failed to process notification: {0}", e.Message);
                    }
                    break;
                case "customer_payment_failed_channel":
                    try
                    {
                        var paymentFailed = ParsePaymentFailedPayload(payload); // Deserialize payload to ProductUpdated object
                        customerService.ProcessPaymentFailed(paymentFailed);
                        this._logger.LogInformation("[ProcessPaymentConfirmed] completed for customer {0}.", paymentFailed.customer.CustomerId);
                        break;
                    }
                    catch (Exception e)
                    {
                        this._logger.LogCritical(e.ToString());
                        this._logger.LogInformation("Failed to process notification: {0}", e.Message);
                    }
                    break;
                case "customer_delivery_channel":
                    try
                    {
                        var deliveryNotification = ParseDeliveryPayload(payload); // Deserialize payload to ProductUpdated object
                        this._logger.LogInformation("[ProcessDeliveryNotification] received for customer {0}", deliveryNotification.customerId);
                        customerService.ProcessDeliveryNotification(deliveryNotification);
                        this._logger.LogInformation("[ProcessDeliveryNotification] completed for customer {0}.", deliveryNotification.customerId);
                        break;
                    }
                    catch (Exception e)
                    {
                        this._logger.LogCritical(e.ToString());
                        this._logger.LogInformation("Failed to process notification: {0}", e.Message);
                    }
                    break;
                
                default:
                    _logger.LogWarning($"Unknown notification channel: {channel}");
                    break;
            }
        }

        private ReserveStockFailed ParseProductUpdatePayload(string payload)
        {
            ReserveStockFailed reserveStockFailed = JsonSerializer.Deserialize<ReserveStockFailed>(payload) ?? throw new InvalidOperationException("Deserialization returned null");
            return reserveStockFailed;
        }

        private PaymentConfirmed ParsePaymentPayload(string payload)
        {
            PaymentConfirmed payment = JsonSerializer.Deserialize<PaymentConfirmed>(payload) ?? throw new InvalidOperationException("Deserialization returned null");
            return payment;
        }

        private PaymentFailed ParsePaymentFailedPayload(string payload)
        {
            PaymentFailed paymentFailed = JsonSerializer.Deserialize<PaymentFailed>(payload) ?? throw new InvalidOperationException("Deserialization returned null");
            return paymentFailed;
        }
        private DeliveryNotification ParseDeliveryPayload(string payload)
        {
            DeliveryNotification deliveryNotification = JsonSerializer.Deserialize<DeliveryNotification>(payload) ?? throw new InvalidOperationException("Deserialization returned null");
            return deliveryNotification;
        }

    }


