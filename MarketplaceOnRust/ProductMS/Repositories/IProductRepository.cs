using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using ProductMS.Models;

namespace ProductMS.Repositories;

public interface IProductRepository
{
    Task RawSQL(string sql);
    Task RawSQLMsg(string sql, params NpgsqlParameter[] parameters);
    void Update(ProductModel product);

    void Insert(ProductModel product);

    ProductModel? GetProduct(int sellerId, int productId);

    ProductModel GetProductForUpdate(int sellerId, int productId);

    IEnumerable<ProductModel> GetBySeller(int sellerId);

    // APIs for ProductService
    IDbContextTransaction BeginTransaction();
    void Reset();
    void Cleanup();

}

