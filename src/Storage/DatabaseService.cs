using System;
using System.Data;
using System.IO;
using System.Reflection;
using DbUp;
using Microsoft.Data.Sqlite;

namespace perinma.Storage;

public class DatabaseService : IDisposable
{
    private readonly string _connectionString;
    private readonly bool _isInMemory;
    private SqliteConnection? _persistentConnection;

    public DatabaseService() : this(inMemory: false)
    {
    }

    public DatabaseService(bool inMemory)
    {
        _isInMemory = inMemory;

        if (inMemory)
        {
            // Use shared cache in-memory database for testing
            var dbName = $"test_{Guid.NewGuid():N}";
            _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

            // Keep a persistent connection open to maintain the in-memory database
            _persistentConnection = new SqliteConnection(_connectionString);
            _persistentConnection.Open();
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dbDirectory = Path.Combine(appData, "perinma");

            if (!Directory.Exists(dbDirectory))
            {
                Directory.CreateDirectory(dbDirectory);
            }

            var databasePath = Path.Combine(dbDirectory, "perinma.db");
            _connectionString = $"Data Source={databasePath}";
        }

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        var upgrader = DeployChanges.To
            .SqliteDatabase(_connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly(), script => script.Contains("Storage.Migrations"))
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            throw new Exception("Database migration failed", result.Error);
        }
    }

    public IDbConnection GetConnection()
    {
        // For both in-memory and file-based modes, return a new connection
        // The in-memory database persists because we keep _persistentConnection open
        return new SqliteConnection(_connectionString);
    }

    public void Dispose()
    {
        _persistentConnection?.Dispose();
    }
}
