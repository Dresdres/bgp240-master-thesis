﻿using Common.Driver;
using System.Text;
using System.Text.Json;
using Common.Entities;
using Common.Events;
using Common.Requests;
using Microsoft.Extensions.Options;
using ProductMS.Infra;
using Npgsql;
using ProductMS.Models;
using ProductMS.Repositories;

namespace ProductMS.Services;

public class ProductService : IProductService
{

    private readonly IProductRepository productRepository;
    private readonly ProductConfig config;
    private readonly ILogger<ProductService> logger;

    public ProductService(IProductRepository productRepository, IOptions<ProductConfig> config, ILogger<ProductService> logger)
    {
        this.productRepository = productRepository;
        this.config = config.Value;
        this.logger = logger;
    }

    private readonly string streamId = new StringBuilder(nameof(TransactionMark)).Append('_').Append(TransactionType.PRICE_UPDATE.ToString()).ToString();

    /*
     * Maybe use postgresql UPSERT: https://stackoverflow.com/questions/1109061/
     */
    public void ProcessCreateProduct(Product product)
    {
        ProductModel input = Utils.AsProductModel(product);
        using (var txCtx = this.productRepository.BeginTransaction()) {
            var existing = this.productRepository.GetProduct(product.seller_id, product.product_id);
            if(existing is null)
                this.productRepository.Insert(input);
            else
                this.productRepository.Update(input);
            txCtx.Commit();
        }
    }

    public async Task ProcessPriceUpdate(PriceUpdate priceUpdate)
    {
        using (var txCtx = this.productRepository.BeginTransaction())
        {
            var product = this.productRepository.GetProductForUpdate(priceUpdate.sellerId, priceUpdate.productId);

            // check if versions match
            if (product.version.SequenceEqual(priceUpdate.version))
            {
                product.price = priceUpdate.price;
                this.productRepository.Update(product);
                txCtx.Commit();
            }

            // must send because some cart items may be old
            if (this.config.Streaming)
            {
                var updateJson = JsonSerializer.Serialize(priceUpdate);
                string sql = "SELECT pg_notify('price_changes', @updateJson)";
                //await productRepository.RawSQL($"SELECT pg_notify('price_update', '{updateJson}')");
                await this.productRepository.RawSQLMsg(sql, new NpgsqlParameter("@updateJson", updateJson));

                //await this.daprClient.PublishEventAsync(PUBSUB_NAME, nameof(PriceUpdated), update);
            }
        }
    }

    public async Task ProcessPoisonPriceUpdate(PriceUpdate priceUpdate)
    {
        if (this.config.Streaming)
        {
            this.logger.LogInformation("Publishing transaction mark {0} to seller {1}", priceUpdate.instanceId, priceUpdate.sellerId);
            await this.productRepository.RawSQL($"SELECT product_add_price_transaction_mark('{streamId}','{priceUpdate.instanceId}','{TransactionType.PRICE_UPDATE}','{priceUpdate.sellerId}','{MarkStatus.ERROR}','product');");
            //await this.daprClient.PublishEventAsync(PUBSUB_NAME, streamId, new TransactionMark(priceUpdate.instanceId, TransactionType.PRICE_UPDATE, priceUpdate.sellerId, MarkStatus.ERROR, "product"));
        }    
    }

    public async Task ProcessProductUpdate(Product product)
    {
        using (var txCtx = this.productRepository.BeginTransaction())
        {
            var oldProduct = this.productRepository.GetProductForUpdate(product.seller_id, product.product_id);
            if (oldProduct is null)
            {
                throw new ApplicationException("Product not found "+product.seller_id +"-" + product.product_id);
            }
            ProductModel input = Utils.AsProductModel(product);

            this.productRepository.Update(input);

            txCtx.Commit();
            if (this.config.Streaming)
            {
                ProductUpdated productUpdated = new(input.seller_id, input.product_id, input.name, input.sku, input.category, input.description, input.price, input.freight_value, input.status, input.version);
                var updateJson = JsonSerializer.Serialize(productUpdated);
                string sql = "SELECT pg_notify('product_changes', @message)";
                await this.productRepository.RawSQLMsg(sql, new NpgsqlParameter("@message", updateJson));
                //await this.daprClient.PublishEventAsync(PUBSUB_NAME, nameof(ProductUpdated), productUpdated);
            }
        }
    }

    public async Task ProcessPoisonProductUpdate(Product product)
    {
        if (this.config.Streaming)
        {
            this.logger.LogInformation("Publishing transaction mark {0} to seller {1}", product.version, product.seller_id);
            await this.productRepository.RawSQL($"SELECT product_add_price_transaction_mark('{streamId}','{product.version}','{TransactionType.PRICE_UPDATE}','{product.seller_id}','{MarkStatus.ERROR}','product');");
            //await this.daprClient.PublishEventAsync(PUBSUB_NAME, streamId, new TransactionMark(product.version, TransactionType.PRICE_UPDATE, product.seller_id, MarkStatus.ERROR, "product"));
        }    
    }

    public void Cleanup()
    {
        this.productRepository.Cleanup();
    }

    public void Reset()
    {
        this.productRepository.Reset();
    }

}
