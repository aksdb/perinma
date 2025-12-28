namespace perinma.Storage;

public class SqliteStorage(DatabaseService databaseService)
{
    private readonly DatabaseService _databaseService = databaseService;
}
