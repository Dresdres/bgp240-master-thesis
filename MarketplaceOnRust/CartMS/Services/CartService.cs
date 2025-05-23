﻿using System.Text;
using CartMS.Infra;
using CartMS.Models;
using CartMS.Repositories;
using Common.Driver;
using Common.Entities;
using Common.Events;
using Common.Requests;
using Npgsql;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CartMS.Services;

public class CartService : ICartService
{
    //private const string PUBSUB_NAME = "pubsub";

    private readonly ICartRepository cartRepository;
    private readonly IProductReplicaRepository productReplicaRepository;

    private readonly CartConfig config;
    private readonly ILogger<CartService> logger;

    public CartService(ICartRepository cartRepository, IProductReplicaRepository productReplicaRepository, IOptions<CartConfig> config, ILogger<CartService> logger)
	{
        //this.daprClient = daprClient;
        this.cartRepository = cartRepository;
        this.productReplicaRepository = productReplicaRepository;
        this.config = config.Value;
        this.logger = logger;
    }

    public void Seal(CartModel cart, bool cleanItems = true)
    {
        cart.status = CartStatus.OPEN;
        if (cleanItems)
        {
            this.cartRepository.Delete(cart.customer_id);
        }
        cart.updated_at = DateTime.UtcNow;
        this.cartRepository.Update(cart);   
    }

    static readonly string checkoutStreamId = new StringBuilder(nameof(TransactionMark)).Append('_').Append(TransactionType.CUSTOMER_SESSION.ToString()).ToString();

    public async Task NotifyCheckout(CustomerCheckout customerCheckout)
    {
        using (var txCtx = this.cartRepository.BeginTransaction())
        {
            List<CartItem> cartItems;
            if(config.ControllerChecks){
                IList<CartItemModel> items = GetItemsWithoutDivergencies(customerCheckout.CustomerId);
                if(items.Count() == 0)
                {
                    throw new ApplicationException($"Cart {customerCheckout.CustomerId} has no items");
                }
                var cart = this.cartRepository.GetCart(customerCheckout.CustomerId);
                if (cart is null) throw new ApplicationException($"Cart {customerCheckout.CustomerId} not found");
                cart.status = CartStatus.CHECKOUT_SENT;
                this.cartRepository.Update(cart);
                cartItems = items.Select(i => new CartItem()
                {
                    SellerId = i.seller_id,
                    ProductId = i.product_id,
                    ProductName = i.product_name is null ? "" : i.product_name,
                    UnitPrice = i.unit_price,
                    FreightValue = i.freight_value,
                    Quantity = i.quantity,
                    Version = i.version,
                    Voucher = i.voucher
                }).ToList();
                this.Seal(cart);
                
            } else {
                IList<CartItemModel> cartItemModels = this.cartRepository.GetItems(customerCheckout.CustomerId);
                this.cartRepository.Delete(customerCheckout.CustomerId);
                cartItems = cartItemModels.Select(i => new CartItem()
                        {
                            SellerId = i.seller_id,
                            ProductId = i.product_id,
                            ProductName = i.product_name is null ? "" : i.product_name,
                            UnitPrice = i.unit_price,
                            FreightValue = i.freight_value,
                            Quantity = i.quantity,
                            Version = i.version,
                            Voucher = i.voucher
                        }).ToList();
            }
            txCtx.Commit();
            if (this.config.Streaming)
            {
                ReserveStock checkout = new ReserveStock(DateTime.UtcNow, customerCheckout, cartItems, customerCheckout.instanceId);
                string checkoutJson = JsonSerializer.Serialize(checkout);
                string sql = "SELECT pg_notify('checkout', @message)";
                await this.cartRepository.RawSQLMsg(sql, new NpgsqlParameter("@message", checkoutJson));
                //await this.daprClient.PublishEventAsync(PUBSUB_NAME, nameof(ReserveStock), checkout);
            }
        }
    }

    public async Task ProcessPoisonCheckout(CustomerCheckout customerCheckout, MarkStatus status)
    {
        if (this.config.Streaming)
        {
            await this.cartRepository.RawSQL($"SELECT cart_add_checkout_transaction_mark('{checkoutStreamId}','{customerCheckout.instanceId}','{TransactionType.CUSTOMER_SESSION}','{customerCheckout.CustomerId}','{status}','cart')");
            //await this.daprClient.PublishEventAsync(PUBSUB_NAME, checkoutStreamId, new TransactionMark(customerCheckout.instanceId, TransactionType.CUSTOMER_SESSION, customerCheckout.CustomerId, status, "cart"));
        }
    }

    private List<CartItemModel> GetItemsWithoutDivergencies(int customerId)
    {
        var items = cartRepository.GetItems(customerId);
        var itemsDict = items.ToDictionary(i => (i.seller_id, i.product_id));

        var ids = items.Select(i => (i.seller_id, i.product_id)).ToList();
        IList<ProductReplicaModel> products = productReplicaRepository.GetProducts(ids);

        foreach (var product in products)
        {
            var item = itemsDict[(product.seller_id, product.product_id)];
            var currPrice = item.unit_price;
            if (item.version.Equals(product.version) && currPrice != product.price)
            {
                itemsDict.Remove((product.seller_id, product.product_id));
            }
        }
        return itemsDict.Values.ToList();
    }

    public async Task ProcessPriceUpdate(PriceUpdated priceUpdate)
    {
        using (var txCtx = this.cartRepository.BeginTransaction())
        {
            if(this.productReplicaRepository.Exists(priceUpdate.seller_id, priceUpdate.product_id))
            {
                 ProductReplicaModel product = this.productReplicaRepository.GetProductForUpdate(priceUpdate.seller_id, priceUpdate.product_id);
                // if not same version, then it has arrived out of order
                if (product.version.SequenceEqual(priceUpdate.version))
                {
                    product.price = priceUpdate.price;
                    this.productReplicaRepository.Update(product);
                }
            }

            // update all carts with such version in any case
            var cartItems = this.cartRepository.GetItemsByProduct(priceUpdate.seller_id, priceUpdate.product_id, priceUpdate.version);
            foreach(var item in cartItems)
            {
                item.unit_price = priceUpdate.price;
                item.voucher += priceUpdate.price - item.unit_price;
            }
            txCtx.Commit();
        }

        if (this.config.Streaming)
        {
            await this.cartRepository.RawSQL($"SELECT cart_add_price_transaction_mark('{priceUpdateStreamId}','{priceUpdate.instanceId}','{TransactionType.PRICE_UPDATE}','{priceUpdate.seller_id}','{MarkStatus.SUCCESS}','cart');");
            //await this.daprClient.PublishEventAsync(PUBSUB_NAME, priceUpdateStreamId, new TransactionMark(priceUpdate.instanceId, TransactionType.PRICE_UPDATE, priceUpdate.seller_id, MarkStatus.SUCCESS, "cart"));
        }

    }

    static readonly string priceUpdateStreamId = new StringBuilder(nameof(TransactionMark)).Append('_').Append(TransactionType.PRICE_UPDATE.ToString()).ToString();

    public async Task ProcessPoisonPriceUpdate(PriceUpdated productUpdate)
    {
        await this.cartRepository.RawSQL($"SELECT cart_add_price_transaction_mark('{priceUpdateStreamId}','{productUpdate.instanceId}','{TransactionType.PRICE_UPDATE}','{productUpdate.seller_id}','{MarkStatus.ABORT}','cart');");
        //await this.daprClient.PublishEventAsync(PUBSUB_NAME, priceUpdateStreamId, new TransactionMark(productUpdate.instanceId, TransactionType.PRICE_UPDATE, productUpdate.seller_id, MarkStatus.ABORT, "cart"));
    }

    public void ProcessProductUpdated(ProductUpdated productUpdated)
    {
        ProductReplicaModel product_ = new()
        {
            seller_id = productUpdated.seller_id,
            product_id = productUpdated.product_id,
            name = productUpdated.name,
            price = productUpdated.price,
            version = productUpdated.version
        };
        if(this.productReplicaRepository.Exists(productUpdated.seller_id, productUpdated.product_id))
        {
            this.productReplicaRepository.Update(product_);
        } else {
            this.productReplicaRepository.Insert(product_);
        }
    }

    public async Task ProcessPoisonProductUpdated(ProductUpdated productUpdate)
    {
        // add_product_transaction_mark
        await this.cartRepository.RawSQL($"SELECT cart_add_price_transaction_mark('{priceUpdateStreamId}','{productUpdate.seller_id}','{TransactionType.UPDATE_PRODUCT}','{productUpdate.version}','{MarkStatus.ABORT}','cart');");
        //await this.daprClient.PublishEventAsync(PUBSUB_NAME, priceUpdateStreamId, new TransactionMark(productUpdate.version, TransactionType.UPDATE_PRODUCT, productUpdate.seller_id, MarkStatus.ABORT, "cart"));
    }

    public void Cleanup()
    {
        this.cartRepository.Cleanup();
        this.productReplicaRepository.Cleanup();
    }

    public void Reset()
    {
        this.cartRepository.Reset();
        this.productReplicaRepository.Reset();
    }

}
