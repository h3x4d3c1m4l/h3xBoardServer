namespace H3xBoardServer.Data;

/// <summary>
/// linq2db data connection. Created fresh per operation via H3xBoardDbFactory
/// (DataConnection is not thread-safe).
/// </summary>
public class H3xBoardDb(DataOptions options) : LinqToDB.Data.DataConnection(options)
{
    public ITable<UserEntity> Users => this.GetTable<UserEntity>();
    public ITable<BoardEntity> Boards => this.GetTable<BoardEntity>();
    public ITable<FileEntity> Files => this.GetTable<FileEntity>();
    public ITable<UserSettingEntity> UserSettings => this.GetTable<UserSettingEntity>();
}

/// <summary>
/// Singleton factory — holds the resolved DataOptions and creates new DB connections on demand.
/// </summary>
public class H3xBoardDbFactory(DataOptions options)
{
    public H3xBoardDb Create() => new(options);
}
