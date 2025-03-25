using Microsoft.EntityFrameworkCore;
using ShipmentMS.Infra;
using Npgsql;
using ShipmentMS.Models;

namespace ShipmentMS.Repositories.Impl;

public class ShipmentRepository : GenericRepository<(int,int), ShipmentModel>, IShipmentRepository
{

    public ShipmentRepository(ShipmentDbContext context) : base(context)
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


    public void Cleanup()
    {
        this.dbSet.ExecuteDelete();
        this.context.SaveChanges();
    }
}
