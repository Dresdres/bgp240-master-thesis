using ShipmentMS.Models;
using Npgsql;

namespace ShipmentMS.Repositories;

public interface IPackageRepository : IRepository<(int,int,int), PackageModel>
{
    Task RawSQL(string sql);
    Task RawSQLMsg(string sql, params NpgsqlParameter[] parameters);
    IDictionary<int, string[]> GetOldestOpenShipmentPerSeller();

    IEnumerable<PackageModel> GetShippedPackagesByOrderAndSeller(int customerId, int orderId, int sellerId);

    int GetTotalDeliveredPackagesForOrder(int customerId, int orderId);

    void Cleanup();
}

