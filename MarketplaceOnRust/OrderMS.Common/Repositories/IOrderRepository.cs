﻿using Microsoft.EntityFrameworkCore.Storage;
using OrderMS.Common.Models;
using Npgsql;

namespace OrderMS.Common.Repositories;

public interface IOrderRepository
{
    Task RawSQL(string sql);
    Task RawSQLMsg(string sql, params NpgsqlParameter[] parameters);
    IEnumerable<OrderModel> GetAll();
    IEnumerable<OrderModel> GetByCustomerId(int customerId);
    OrderModel? GetOrder(int customerId, int orderId);
    OrderModel InsertOrder(OrderModel order);
    OrderModel UpdateOrder(OrderModel order);

    // APIs for OrderService
    IDbContextTransaction BeginTransaction();
    CustomerOrderModel? GetCustomerOrderByCustomerId(int customerId);
    CustomerOrderModel InsertCustomerOrder(CustomerOrderModel customerOrder);
    CustomerOrderModel UpdateCustomerOrder(CustomerOrderModel customerOrder);
    OrderItemModel InsertOrderItem(OrderItemModel orderItem);
    OrderHistoryModel InsertOrderHistory(OrderHistoryModel orderHistory);
    void FlushUpdates();
    void Cleanup();

}


