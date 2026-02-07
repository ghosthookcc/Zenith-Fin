using Npgsql;
using System.Data;

namespace ZenithFin.PostgreSQL
{
    public interface IDatabaseConnectionFactory
    {
        IDbConnection Create();
    }
    public class DatabaseConnectionFactory : IDatabaseConnectionFactory
    {
        private readonly string _connectionString;

        public DatabaseConnectionFactory(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection")!;
        }

        public IDbConnection Create() => new NpgsqlConnection(_connectionString);
    }
}
