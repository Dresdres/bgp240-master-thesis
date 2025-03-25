using Common.Entities;
using Microsoft.EntityFrameworkCore;
using ShipmentMS.Infra;
using Npgsql;
using ShipmentMS.Models;

namespace ShipmentMS.Repositories.Impl;

public class PackageRepository : GenericRepository<(int,int,int), PackageModel>, IPackageRepository
{

    public PackageRepository(ShipmentDbContext context) : base(context)
	{
    }

    public async Task RawSQL(string sql)
    {
        await this.context.Database.ExecuteSqlRawAsync(sql);
    }

    public async Task RawSQLMsg(string sql, params NpgsqlParameter[] parameters)
    {
        await this.context.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    public IDictionary<int, string[]> GetOldestOpenShipmentPerSeller()
    {
        return this.dbSet
                        .Where(x => x.status.Equals(PackageStatus.shipped))
                        .GroupBy(x => x.seller_id)
                        .Select(g => new { key = g.Key, minOrderId = g.Min(x => x.order_id), minCustomerId = g.Min(x => x.customer_id) }) // Get min order_id and customer_id//, Sort = g.Min(x => x.GetOrderIdAsString()) }).Take(10)
                        .AsEnumerable()
                        .ToDictionary(g => g.key, g => {
                            if (g.minOrderId == 0 || g.minCustomerId == 0) return Array.Empty<string>();  //Handle the case where Min returns default value
                                return new[] { $"{g.minCustomerId}|{g.minOrderId}" 
                            }; // Concatenate on the client side
        });
    }

    public IEnumerable<PackageModel> GetShippedPackagesByOrderAndSeller(int customerId, int orderId, int sellerId)
    {
        return this.dbSet.Where(p => p.customer_id == customerId && p.order_id == orderId && p.status == PackageStatus.shipped && p.seller_id == sellerId);
    }

    public int GetTotalDeliveredPackagesForOrder(int customerId, int orderId)
    {
        return this.dbSet.Where(p => p.customer_id == customerId && p.order_id == orderId && p.status == PackageStatus.delivered).Count();
    }

    public void Cleanup()
    {
        this.dbSet.ExecuteDelete();
        this.context.SaveChanges();
    }
}

