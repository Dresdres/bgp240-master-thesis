﻿using System.Data;
using System.Diagnostics;
using System.Text;
using Common.Entities;
using Microsoft.EntityFrameworkCore;
using OrderMS.Common.Models;

namespace OrderMS.Test;

public class TransactionTest : IClassFixture<TestDatabaseFixture>
{
    public TransactionTest(TestDatabaseFixture fixture) => Fixture = fixture;

    public TestDatabaseFixture Fixture { get; }

    [Fact]
    public void TestOrderCriticalPath()
    {
        int tasks = 100;
        CountdownEvent ctd = new CountdownEvent(tasks);

        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();
        for (int i = 0; i < tasks; i++)
            Task.Run(() => CreateOrder(ctd));

        ctd.Wait();
        stopwatch.Stop();
        Console.WriteLine("Time elapsed: {0}", stopwatch.ElapsedMilliseconds);
    }

    private void CreateOrder(CountdownEvent ctd)
    {
        int customer_id = 1;
        try
        {
            var now = DateTime.UtcNow;
            using var dbContext = Fixture.GetContext();
            using (var transaction = dbContext.Database.BeginTransaction()){
                var customerOrder = dbContext.CustomerOrders.FromSqlRaw(string.Format("SELECT co.* FROM customer_orders AS co WHERE co.customer_id = {0} FOR UPDATE", customer_id));
                CustomerOrderModel com;
                if (customerOrder is null || customerOrder.Count() == 0)
                {
                    com = new()
                    {
                        customer_id = customer_id,
                        next_order_id = 1
                    };
                    com = dbContext.CustomerOrders.Add(com).Entity;
                }
                else
                {
                    com = customerOrder.First();
                    com.next_order_id += 1;
                    dbContext.CustomerOrders.Update(com);
                    //https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete
                    // dbContext.CustomerOrders.ExecuteUpdate(setters => setters.SetProperty(p => p.next_order_id, p => p.next_order_id + 1));
                    // result is not returned. and update is not tracked
                    // customerOrder = dbContext.CustomerOrders.Where(e => e.customer_id == customer_id).First();
                }

                StringBuilder stringBuilder = new StringBuilder().Append(1)
                                                                    .Append("-").Append(now.ToString("d"))
                                                                    .Append("-").Append(com.next_order_id);

                var order = new OrderModel() { invoice_number = stringBuilder.ToString(), purchase_date = DateTime.UtcNow, customer_id = 1, count_items = 0, created_at = DateTime.UtcNow, updated_at = DateTime.UtcNow };

                var orderPersisted = dbContext.Orders.Add(order);
                dbContext.SaveChanges();

                dbContext.OrderHistory.Add(new OrderHistoryModel()
                {
                    order_id = com.next_order_id,
                    created_at = orderPersisted.Entity.created_at,
                    status = OrderStatus.INVOICED
                });

                dbContext.SaveChanges();
                transaction.Commit();
            }

            // retrieve order in another transaction
            using (var transaction = dbContext.Database.BeginTransaction())
            {
                var com = dbContext.CustomerOrders.Find(1);
                if(com is not null)
                    Console.WriteLine("Expected: {0} Retrieved {1}", 1, com.next_order_id);
            }

        }
        catch (Exception e) {
            Console.WriteLine("Error: {0}", e.Message);
        }
        finally { ctd.Signal(); }
    }

    [Fact]
    public void TestTransaction()
	{
        int tasks = 1;
        CountdownEvent ctd = new CountdownEvent(tasks);
        using var context = Fixture.GetContext();
        // set order table to unlogged
        context.Database.ExecuteSqlRaw("ALTER TABLE \"order\".order_history SET unlogged");
        context.Database.ExecuteSqlRaw("ALTER TABLE \"order\".order_items SET unlogged");
        context.Database.ExecuteSqlRaw("ALTER TABLE \"order\".customer_orders SET unlogged");
        context.Database.ExecuteSqlRaw("ALTER TABLE \"order\".orders SET UNLOGGED");

        for (int i = 0; i < tasks; i++)
        {
            Task.Run(() => CreateSimpleOrder(ctd));
        }

        ctd.Wait();
    }

    private void CreateSimpleOrder(CountdownEvent ctd)
    {
        try
        {
            int id;
            using var dbContext = Fixture.GetContext();
            using (var transaction = dbContext.Database.BeginTransaction(IsolationLevel.Serializable))
            {
                var order = new OrderModel() { order_id = 1, purchase_date = DateTime.UtcNow, customer_id = 1, count_items = 0, created_at = DateTime.UtcNow, updated_at = DateTime.UtcNow };
                var tracking = dbContext.Orders.Add(order);
                dbContext.SaveChanges();
                id = order.order_id;
                Console.WriteLine("ID returned: {0}", id);
                transaction.Commit();
            }

            using (var transaction = dbContext.Database.BeginTransaction())
            {
                var orderRes = dbContext.Orders.First();
                Console.WriteLine("Expected: {0} Retrieved {1}", 1, orderRes.order_id);
            }

        }
        catch (Exception) { }
        finally
        {
            ctd.Signal();
        }
    }
}

