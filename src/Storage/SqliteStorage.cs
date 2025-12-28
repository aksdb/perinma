namespace perinma.Storage;

public class SqliteStorage
{
    private readonly DatabaseService _databaseService;

    public SqliteStorage(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

}
