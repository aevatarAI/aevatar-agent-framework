using Microsoft.Data.Sqlite;

namespace Aevatar.Agents.Persistence.SQLite;

/// <summary>
/// SQLite connection factory (lightweight, create-per-operation).
/// </summary>
public sealed class SQLiteConnectionFactory
{
    public SQLiteConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("connectionString cannot be null/empty.", nameof(connectionString));
        }

        ConnectionString = connectionString.Trim();
    }

    public string ConnectionString { get; }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection(ConnectionString);
    }
}
