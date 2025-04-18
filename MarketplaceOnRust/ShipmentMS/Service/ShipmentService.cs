﻿using System.Data;
using System.Text;
using Common.Driver;
using Common.Entities;
using Common.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShipmentMS.Infra;
using ShipmentMS.Models;
using ShipmentMS.Repositories;
using System.Text.Json;
using Npgsql;

namespace ShipmentMS.Service;

public class ShipmentService : IShipmentService
{
    private readonly IShipmentRepository shipmentRepository;
    private readonly IPackageRepository packageRepository;
    private readonly ShipmentConfig config;
    private readonly ILogger<ShipmentService> logger;

    public ShipmentService(IShipmentRepository shipmentRepository, IPackageRepository packageRepository, IOptions<ShipmentConfig> config,ILogger<ShipmentService> logger)
    {
        this.shipmentRepository = shipmentRepository;
        this.packageRepository = packageRepository;
        this.config = config.Value;
        this.logger = logger;
    }

    /*
     * https://twitter.com/hnasr/status/1657569218609684480
     */
    public async Task ProcessShipment(PaymentConfirmed paymentConfirmed)
    {
        using (var txCtx = this.shipmentRepository.BeginTransaction())
        {
            DateTime now = DateTime.UtcNow;
            ShipmentModel shipment = new()
            {
                order_id = paymentConfirmed.orderId,
                customer_id = paymentConfirmed.customer.CustomerId,
                package_count = paymentConfirmed.items.Count,
                total_freight_value = paymentConfirmed.items.Sum(i => i.freight_value),
                request_date = now,
                status = ShipmentStatus.approved,
                first_name = paymentConfirmed.customer.FirstName,
                last_name = paymentConfirmed.customer.LastName,
                street = paymentConfirmed.customer.Street,
                complement = paymentConfirmed.customer.Complement,
                zip_code = paymentConfirmed.customer.ZipCode,
                city = paymentConfirmed.customer.City,
                state = paymentConfirmed.customer.State
            };

            this.shipmentRepository.Insert(shipment);

            int package_id = 1;
            List<PackageModel> packageModels = new();
            foreach (var item in paymentConfirmed.items)
            {
                PackageModel package = new()
                {
                    customer_id = paymentConfirmed.customer.CustomerId,
                    order_id = paymentConfirmed.orderId,
                    package_id = package_id,
                    status = PackageStatus.shipped,
                    freight_value = item.freight_value,
                    shipping_date = now,
                    seller_id = item.seller_id,
                    product_id = item.product_id,
                    product_name = item.product_name,
                    quantity = item.quantity
                };
                packageModels.Add(package);
                package_id++;
            }

            this.packageRepository.InsertAll(packageModels);
            // both repositories are part of the same context, so either call works
            this.packageRepository.Save();
            txCtx.Commit();

            // enqueue shipment notification
            if (this.config.Streaming)
            {
                ShipmentNotification shipmentNotification = new ShipmentNotification(paymentConfirmed.customer.CustomerId, paymentConfirmed.orderId, now, paymentConfirmed.instanceId, ShipmentStatus.approved);
                string shipmentJson = JsonSerializer.Serialize(shipmentNotification);
                string sql = "SELECT pg_notify('shipment', @shipmentJson)";
                await this.shipmentRepository.RawSQLMsg(sql, new NpgsqlParameter("@shipmentJson", shipmentJson));

                await this.shipmentRepository.RawSQL($"SELECT shipment_add_checkout_transaction_mark('{streamId}','{paymentConfirmed.instanceId}','{TransactionType.CUSTOMER_SESSION}','{paymentConfirmed.customer.CustomerId}','{MarkStatus.SUCCESS}','shipment');");

                    //this.daprClient.PublishEventAsync(PUBSUB_NAME, nameof(ShipmentNotification), shipmentNotification),
                    // publish transaction event result
                    //this.daprClient.PublishEventAsync(PUBSUB_NAME, streamId, new TransactionMark(paymentConfirmed.instanceId, TransactionType.CUSTOMER_SESSION, paymentConfirmed.customer.CustomerId, MarkStatus.SUCCESS, "shipment"))
            }
        }
    }

    static readonly string streamId = new StringBuilder(nameof(TransactionMark)).Append('_').Append(TransactionType.CUSTOMER_SESSION.ToString()).ToString();

    public async Task ProcessPoisonShipment(PaymentConfirmed paymentConfirmed)
    {
        await this.shipmentRepository.RawSQL($"SELECT shipment_add_checkout_transaction_mark('{streamId}','{paymentConfirmed.instanceId}','{TransactionType.CUSTOMER_SESSION}','{paymentConfirmed.customer.CustomerId}','{MarkStatus.ABORT}','shipment');");
        //await this.daprClient.PublishEventAsync(PUBSUB_NAME, streamId, new TransactionMark(paymentConfirmed.instanceId, TransactionType.CUSTOMER_SESSION, paymentConfirmed.customer.CustomerId, MarkStatus.ABORT, "shipment"));
    }

    // update delivery status of many packages
    public async Task UpdateShipment(string instanceId)
    {
        using (var txCtx = this.shipmentRepository.BeginTransaction(IsolationLevel.Serializable))
        {
            // perform a sql query to query the objects. write lock...
            var q = packageRepository.GetOldestOpenShipmentPerSeller();

            foreach (var kv in q)
            {
                var packages_ = this.packageRepository.GetShippedPackagesByOrderAndSeller(int.Parse( kv.Value[0] ), int.Parse(kv.Value[1]), kv.Key).ToList();
                if (packages_.Count() == 0)
                {
                    this.logger.LogWarning("No packages retrieved from the DB for seller {0}", kv.Key);
                    continue;
                }
                await UpdatePackageDelivery(packages_, instanceId);
            }

            txCtx.Commit();
        }
    }

    private async Task UpdatePackageDelivery(List<PackageModel> sellerPackages, string instanceId)
    {
        int customerId = sellerPackages.ElementAt(0).customer_id;
        int orderId = sellerPackages.ElementAt(0).order_id;
        var id = (customerId,orderId);
        ShipmentModel? shipment = this.shipmentRepository.GetById(id) ?? throw new Exception("Shipment ID " + id + " cannot be found in the database!");
        List<Task> tasks = new(sellerPackages.Count + 1);
        var now = DateTime.UtcNow;
        if (shipment.status == ShipmentStatus.approved)
        {
            shipment.status = ShipmentStatus.delivery_in_progress;
            this.shipmentRepository.Update(shipment);
            this.shipmentRepository.Save();
            ShipmentNotification shipmentNotification = new ShipmentNotification(
                    shipment.customer_id, shipment.order_id, now, instanceId, ShipmentStatus.delivery_in_progress);
            string shipmentJson = JsonSerializer.Serialize(shipmentNotification);
            string sql = "SELECT pg_notify('shipment', @shipmentJson)";
            tasks.Add(this.shipmentRepository.RawSQLMsg(sql, new NpgsqlParameter("@shipmentJson", shipmentJson)));
                //this.daprClient.PublishEventAsync(PUBSUB_NAME, nameof(ShipmentNotification), shipmentNotification));
        }

        // aggregate operation
        int countDelivered = this.packageRepository.GetTotalDeliveredPackagesForOrder(shipment.customer_id, shipment.order_id);

        this.logger.LogDebug("Count delivery for shipment id {0}-{1}: {2} total of {3}",
                shipment.customer_id, shipment.order_id, countDelivered, shipment.package_count);

        foreach (var package in sellerPackages)
        {
            package.status = PackageStatus.delivered;
            package.delivery_date = now;
            this.packageRepository.Update(package);
            var delivery = new DeliveryNotification(
                shipment.customer_id, package.order_id, package.package_id, package.seller_id,
                package.product_id, package.product_name, PackageStatus.delivered, now, instanceId);
            string deliveryJson = JsonSerializer.Serialize(delivery);
            string sql = "SELECT pg_notify('delivery', @deliveryJson)";
            tasks.Add(this.packageRepository.RawSQLMsg(sql, new NpgsqlParameter("@deliveryJson", deliveryJson)));
                //this.daprClient.PublishEventAsync(PUBSUB_NAME, nameof(DeliveryNotification), delivery));
        }
        this.packageRepository.Save();

        if (shipment.package_count == countDelivered + sellerPackages.Count())
        {
            this.logger.LogDebug("Delivery concluded for shipment id {1}", shipment.order_id);
            shipment.status = ShipmentStatus.concluded;
            this.shipmentRepository.Update(shipment);
            this.shipmentRepository.Save();
            ShipmentNotification shipmentNotification = new ShipmentNotification(
                shipment.customer_id, shipment.order_id, now, instanceId, ShipmentStatus.concluded);
            string shipmentJson = JsonSerializer.Serialize(shipmentNotification);
            string sql = "SELECT pg_notify('shipment', @shipmentJson)";
            tasks.Add(this.shipmentRepository.RawSQLMsg(sql, new NpgsqlParameter("@shipmentJson", shipmentJson)));
                //this.daprClient.PublishEventAsync(PUBSUB_NAME, nameof(ShipmentNotification), shipmentNotification));
        }
        else
        {
            this.logger.LogDebug("Delivery not yet concluded for shipment id {1}: count {2} of total {3}",
                    shipment.order_id, countDelivered + sellerPackages.Count(), shipment.package_count);
        }

        await Task.WhenAll(tasks);
    }

    public void Cleanup()
    {
        this.shipmentRepository.Cleanup();
        this.shipmentRepository.Cleanup();
    }

}

