﻿using Microsoft.EntityFrameworkCore.Storage;
using StockMS.Models;
using Npgsql;

namespace StockMS.Repositories;

public interface IStockRepository
{
    Task RawSQL(string sql);
    Task RawSQLMsg(string sql, params NpgsqlParameter[] parameters);
    StockItemModel Insert(StockItemModel item);

    void Update(StockItemModel item);

    StockItemModel? Find(int sellerId, int productId);

    IEnumerable<StockItemModel> GetAll();

    IEnumerable<StockItemModel> GetItems(List<(int SellerId, int ProductId)> ids);
    IEnumerable<StockItemModel> GetBySellerId(int sellerId);

    StockItemModel FindForUpdate(int seller_id, int product_id);

    // APIs for StockService
    IDbContextTransaction BeginTransaction();
    void FlushUpdates();
    void UpdateRange(List<StockItemModel> stockItemsReserved);
    void Reset(int qty);
    void Cleanup();
}

