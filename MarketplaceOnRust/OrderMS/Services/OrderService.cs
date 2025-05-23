﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using System.Globalization;
using Common.Entities;
using Common.Events;
using Common.Driver;
using OrderMS.Common.Models;
using OrderMS.Common.Repositories;
using OrderMS.Services;
using OrderMS.Common.Infra;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Npgsql;
using Microsoft.Extensions.ObjectPool;

namespace OrderMS.Handlers;

public class OrderService : IOrderService
{
    private static readonly CultureInfo enUS = CultureInfo.CreateSpecificCulture("en-US");
    private static readonly DateTimeFormatInfo dtfi = enUS.DateTimeFormat;

    static OrderService()
    {
        // https://learn.microsoft.com/en-us/dotnet/api/system.globalization.datetimeformatinfo?view=net-7.0
        dtfi.ShortDatePattern = "yyyyMMdd";
    }

    private readonly IOrderRepository orderRepository;
    private readonly OrderConfig config;
    private readonly ILogger<OrderService> logger;

    private static readonly float[] emptyArray = Array.Empty<float>();

    public OrderService(IOrderRepository orderRepository,
            IOptions<OrderConfig> config, ILogger<OrderService> logger)
    {
        this.orderRepository = orderRepository;
        this.config = config.Value;
        this.logger = logger;
    }

    public async Task ProcessStockConfirmed(StockConfirmed checkout)
	{
        // https://learn.microsoft.com/en-us/ef/ef6/saving/transactions?redirectedfrom=MSDN
        using (var txCtx = this.orderRepository.BeginTransaction())
        {
            var now = DateTime.UtcNow;

            // calculate total freight_value
            float total_freight = 0;
            float total_amount = 0;
            foreach (var item in checkout.items)
            {
                total_freight += item.FreightValue;
                total_amount += (item.UnitPrice * item.Quantity);
            }

            // total before discounts
            float total_items = total_amount;

            // apply vouchers per product, but only until total >= 0 for each item
            // https://www.amazon.com/gp/help/customer/display.html?nodeId=G9R2MLD3EX557D77
            Dictionary<(int,int), float> totalPerItem = new();
            float total_incentive = 0;
            foreach(var item in checkout.items)
            {
                float total_item = item.UnitPrice * item.Quantity;

                if (total_item - item.Voucher > 0)
                {
                    total_amount -= item.Voucher;
                    total_incentive += item.Voucher;
                    total_item -= item.Voucher;
                }
                else
                {
                    total_amount -= total_item;
                    total_incentive += total_item;
                    total_item = 0;
                }
                    
                totalPerItem.Add((item.SellerId, item.ProductId), total_item);
            }

            // https://finom.co/en-fr/blog/invoice-number/
            // postresql does not give us sequence ids. it is interesting for the analyst to get
            // a sense of how much orders this customer has made by simply looking at patterns in
            // the invoice. the format <customer_id>-<int_date>-total_orders+1 can be represented like:
            // 50-20220928-001
            // it is inefficient to get count(*) on customer orders. better to have a table like tpc-c does for next_order_id
            var customerOrder = this.orderRepository.GetCustomerOrderByCustomerId(checkout.customerCheckout.CustomerId);
            if (customerOrder is null)
            {
                customerOrder = new()
                {
                    customer_id = checkout.customerCheckout.CustomerId,
                    next_order_id = 1
                };
                customerOrder = this.orderRepository.InsertCustomerOrder(customerOrder);
            }
            else
            {
                customerOrder.next_order_id += 1;
                customerOrder = this.orderRepository.UpdateCustomerOrder(customerOrder);
            }

            StringBuilder stringBuilder = new StringBuilder().Append(checkout.customerCheckout.CustomerId)
                                                                .Append('-').Append(now.ToString("d", enUS))
                                                                .Append('-').Append(customerOrder.next_order_id);

            OrderModel newOrder = new()
            {
                customer_id = checkout.customerCheckout.CustomerId,
                order_id = customerOrder.next_order_id,
                invoice_number = stringBuilder.ToString(),
                // olist have seller acting in the approval process
                // here we approve automatically
                // besides, invoice is a request for payment, so it makes sense to use this status now
                status = OrderStatus.INVOICED,
                purchase_date = checkout.timestamp,
                total_amount = total_amount,
                total_items = total_items,
                total_freight = total_freight,
                total_incentive = total_incentive,
                total_invoice = total_amount + total_freight,
                count_items = checkout.items.Count(),
                created_at = now,
                updated_at = now
            };
            var orderPersisted = this.orderRepository.InsertOrder(newOrder);

            // save to ensure FK
            this.orderRepository.FlushUpdates();

            List<OrderItem> orderItems = new(checkout.items.Count);

            int id = 1;
            foreach (var item in checkout.items)
            {
                OrderItemModel oim = new()
                {
                    customer_id = checkout.customerCheckout.CustomerId,
                    order_id = customerOrder.next_order_id,
                    order_item_id = id,
                    product_id = item.ProductId,
                    product_name = item.ProductName,
                    seller_id = item.SellerId,
                    unit_price = item.UnitPrice,
                    quantity = item.Quantity,
                    total_items = item.UnitPrice * item.Quantity,
                    total_amount = totalPerItem[(item.SellerId, item.ProductId)],
                    freight_value = item.FreightValue,
                    shipping_limit_date = now.AddDays(3)
                };

                this.orderRepository.InsertOrderItem(oim);

                // vouchers so payment can process
                orderItems.Add(AsOrderItem(oim, item.Voucher));

                id++;
            }

            // initialize order history
             this.orderRepository.InsertOrderHistory(new OrderHistoryModel()
            {
                customer_id = orderPersisted.customer_id,
                order_id = orderPersisted.order_id,
                created_at = newOrder.created_at,
                status = OrderStatus.INVOICED,
                order = orderPersisted // not sure why it is necessary to attach this object in order history and not in order item, but guess it is the id generated in order history. the id generation happens in the database level
            });

            this.orderRepository.FlushUpdates();
            txCtx.Commit();

            // if the event is published and the transaction fails to commit (e.g., concurrency exception)
            // the dapr will retry the event processing and the invoiceIssued event will be duplicated in the system
            if (this.config.Streaming)
            {

                InvoiceIssued invoice = new InvoiceIssued(checkout.customerCheckout, customerOrder.next_order_id, newOrder.invoice_number, now, newOrder.total_invoice, orderItems, checkout.instanceId);
                string invoiceJson = JsonSerializer.Serialize(invoice);
                string sql = "SELECT pg_notify('invoice_issued', @message)";
                await this.orderRepository.RawSQLMsg(sql, new NpgsqlParameter("@message", invoiceJson));
                //await this.daprClient.PublishEventAsync(PUBSUB_NAME, nameof(InvoiceIssued), invoice);
            }
        }

	}

    static readonly string streamId = new StringBuilder(nameof(TransactionMark)).Append('_').Append(TransactionType.CUSTOMER_SESSION.ToString()).ToString();

    public async Task ProcessPoisonStockConfirmed(StockConfirmed stockConfirmed)
    {
        await this.orderRepository.RawSQL($"SELECT order_add_checkout_transaction_mark('{streamId}','{stockConfirmed.instanceId}','{TransactionType.CUSTOMER_SESSION}','{stockConfirmed.customerCheckout.CustomerId}','{MarkStatus.ABORT}','order');");
        //await this.daprClient.PublishEventAsync(PUBSUB_NAME, streamId, new TransactionMark(stockConfirmed.instanceId, TransactionType.CUSTOMER_SESSION, stockConfirmed.customerCheckout.CustomerId, MarkStatus.ABORT, "order"));
    }

    private static OrderItem AsOrderItem(OrderItemModel orderItem, float voucher)
    {
        return new()
        {
            order_id = orderItem.order_id,
            order_item_id = orderItem.order_item_id,
            product_id = orderItem.product_id,
            product_name = orderItem.product_name,
            seller_id = orderItem.seller_id,
            unit_price = orderItem.unit_price,
            quantity = orderItem.quantity,
            total_items = orderItem.total_items,
            total_amount = orderItem.total_amount,
            shipping_limit_date = orderItem.shipping_limit_date,
            freight_value = orderItem.freight_value,
            total_incentive = voucher
        };
    }

    private static Order AsOrder(OrderModel orderModel)
    {
        return new()
        {
            customer_id = orderModel.customer_id,
            order_id = orderModel.order_id,
            status = orderModel.status,
            created_at = orderModel.created_at,
            purchase_date = orderModel.purchase_date,
            total_amount = orderModel.total_amount,
            total_items = orderModel.total_items,
            total_freight = orderModel.total_freight,
            total_incentive = orderModel.total_incentive,
            total_invoice = orderModel.total_invoice,
            count_items = orderModel.count_items,
        }; 
    }

    public void ProcessPaymentConfirmed(PaymentConfirmed paymentConfirmed)
    {
        var now = DateTime.UtcNow;
        using (var txCtx = this.orderRepository.BeginTransaction())
        {
            OrderModel? order = this.orderRepository.GetOrder(paymentConfirmed.customer.CustomerId, paymentConfirmed.orderId);
            if(order is null)
            {
                throw new Exception($"Cannot find order {paymentConfirmed.customer.CustomerId}-{paymentConfirmed.orderId}");
            }
            order.status = OrderStatus.PAYMENT_PROCESSED;
            order.payment_date = paymentConfirmed.date;
            order.updated_at = now;

            this.orderRepository.UpdateOrder(order);

            this.orderRepository.InsertOrderHistory(new OrderHistoryModel()
            {
                order_id = paymentConfirmed.orderId,
                created_at = now,
                status = OrderStatus.PAYMENT_PROCESSED,
                order = order
            });

            this.orderRepository.FlushUpdates();
            txCtx.Commit();
        }
    }

    public void ProcessPaymentFailed(PaymentFailed paymentFailed)
    {
        var now = DateTime.UtcNow;
        using (var txCtx = this.orderRepository.BeginTransaction())
        {
            OrderModel? order = this.orderRepository.GetOrder(paymentFailed.customer.CustomerId, paymentFailed.orderId);
            if (order is null)
            {
                throw new Exception($"Cannot find order {paymentFailed.customer.CustomerId}-{paymentFailed.orderId}");
            }

            order.status = OrderStatus.PAYMENT_FAILED;
            order.updated_at = now;

            this.orderRepository.UpdateOrder(order);

            this.orderRepository.InsertOrderHistory(new OrderHistoryModel()
            {
                order_id = paymentFailed.orderId,
                created_at = now,
                status = OrderStatus.PAYMENT_FAILED,
                order = order
            });

            this.orderRepository.FlushUpdates();
            txCtx.Commit();
        }
    }

    public void ProcessShipmentNotification(ShipmentNotification shipmentNotification)
    {
        DateTime now = DateTime.UtcNow;
        using (var txCtx = this.orderRepository.BeginTransaction())
        {
            OrderModel? order = this.orderRepository.GetOrder(shipmentNotification.customerId, shipmentNotification.orderId);
            if (order is null)
            {
                throw new Exception($"Cannot find order {shipmentNotification.customerId}-{shipmentNotification.orderId}");
            }

            OrderStatus orderStatus = OrderStatus.READY_FOR_SHIPMENT;
            if (shipmentNotification.status == ShipmentStatus.delivery_in_progress){
                orderStatus = OrderStatus.IN_TRANSIT;
                order.delivered_carrier_date = shipmentNotification.eventDate;
            }
            if (shipmentNotification.status == ShipmentStatus.concluded){
                orderStatus = OrderStatus.DELIVERED;
                order.delivered_customer_date = shipmentNotification.eventDate;
            }

            OrderHistoryModel orderHistory = new()
            {
                order_id = shipmentNotification.orderId,
                created_at = now,
                status = orderStatus,
                order = order
            };

            order.status = orderStatus;
            order.updated_at = now;

            this.orderRepository.UpdateOrder(order);
            this.orderRepository.InsertOrderHistory(orderHistory);

            this.orderRepository.FlushUpdates();
            txCtx.Commit();
        }
    }

    public void Cleanup()
    {
        this.orderRepository.Cleanup();
    }

}

