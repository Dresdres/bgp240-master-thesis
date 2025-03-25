using ShipmentMS.Models;
using Npgsql;

namespace ShipmentMS.Repositories;

public interface IShipmentRepository : IRepository<(int,int), ShipmentModel>
{
    Task RawSQL(string sql);
    Task RawSQLMsg(string sql, params NpgsqlParameter[] parameters);
    void Cleanup();
}
