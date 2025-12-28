using System;
using System.Data;
using System.IO;
using System.Reflection;
using DbUp;
using Microsoft.Data.Sqlite;

namespace perinma.Storage;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dbDirectory = Path.Combine(appData, "perinma");
        
        if (!Directory.Exists(dbDirectory))
        {
            Directory.CreateDirectory(dbDirectory);
        }

        var databasePath = Path.Combine(dbDirectory, "perinma.db");
        _connectionString = $"Data Source={databasePath}";
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
        return new SqliteConnection(_connectionString);
    }
}
