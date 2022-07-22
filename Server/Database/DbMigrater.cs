using FileFlows.Server.Database.Managers;
using FileFlows.Shared.Models;
using Microsoft.Data.SqlClient;
using DatabaseType = NPoco.DatabaseType;

namespace FileFlows.Server.Database;

/// <summary>
/// Migrates one database to another database
/// </summary>
public class DbMigrater
{
    /// <summary>
    /// Migrates data from one database to another
    /// </summary>
    /// <param name="sourceConnection">the connection string of the source database</param>
    /// <param name="destinationConnection">the connection string of the destination database</param>
    /// <returns>if the migration was successful</returns>
    public static bool Migrate(string sourceConnection, string destinationConnection)
    {
        try
        {
            Logger.Instance?.ILog("Database Migration started");

            using var source = GetDatabase(sourceConnection);

            if (destinationConnection.Contains("sqlite"))
            {
                // move the db if it exists so we can create a new one
                SqliteDbManager.MoveFileFromConnectionString(destinationConnection);
            }
            var destDbManager = DbManager.GetManager(destinationConnection);
            destDbManager.CreateDb(recreate: true, insertInitialData: false).Wait();
            using var dest = GetDatabase(destinationConnection);

            MigrateDbObjects(source, dest);
            MigrateDbStatistics(source, dest);
            MigrateRevisions(source, dest);
            MigrateDbLogs(source, dest);

            Logger.Instance?.ILog("Database Migration complete");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Instance.ELog("Failed to migrate data: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Checks if the external database already exists and is a FileFlows DB
    /// </summary>
    /// <param name="connectionString">the connection string</param>
    /// <returns>if the database exists or not</returns>
    public static bool ExternalDatabaseExists(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString.ToLower().IndexOf(".sqlite") > 0)
            return false; // never treat SQL lite as existing

        try
        {
            var db = DbManager.GetManager(connectionString);
            var settings = db.Single<Settings>().Result;
            return string.IsNullOrEmpty(settings?.Version) == false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void MigrateDbObjects(NPoco.Database source, NPoco.Database dest)
    {
        var dbObjects = source.Fetch<DbObject>($"select * from {nameof(DbObject)}")?.ToArray();
        if (dbObjects?.Any() != true)
            return;

        foreach (var obj in dbObjects)
        {
            Logger.Instance?.DLog($"Migrating [{obj.Uid}][{obj.Type}]: {obj.Name ?? string.Empty}");

            try
            {
                dest.Execute(
                    $"insert into {nameof(DbObject)} (Uid, Name, Type, DateCreated, DateModified, Data) values (@0, @1, @2, @3, @4, @5)",
                    obj.Uid.ToString(),
                    obj.Name,
                    obj.Type,
                    obj.DateCreated,
                    obj.DateModified,
                    obj.Data ?? string.Empty);
            }
            catch (Exception ex)
            {
                Logger.Instance.ELog("Failed migrating: " +  ex.Message);
                Logger.Instance.ELog("Migration Object: " + JsonSerializer.Serialize(obj));
                throw;
            }
        }
    }

    private static void MigrateDbStatistics(NPoco.Database source, NPoco.Database dest)
    {
        var dbStatistics = source.Fetch<DbStatistic>($"select * from {nameof(DbStatistic)}")?.ToArray();
        if (dbStatistics?.Any() != true)
            return;

        foreach (var obj in dbStatistics)
        {
            try
            {
                dest.Execute(
                    $"insert into {nameof(DbStatistic)} (LogDate, Name, Type, StringValue, NumberValue) values (@0, @1, @2, @3, @4)",
                    obj.LogDate,
                    obj.Name,
                    (int)obj.Type,
                    obj.StringValue ?? string.Empty,
                    obj.NumberValue);
            }
            catch (Exception)
            {
            }
        }
    }
    
    private static void MigrateRevisions(NPoco.Database source, NPoco.Database dest)
    {
        var dbRevisions = source.Fetch<RevisionedObject>($"select * from {nameof(RevisionedObject)}")?.ToArray();
        if (dbRevisions?.Any() != true)
            return;

        foreach (var obj in dbRevisions)
        {
            try
            {
                dest.Execute(
                    $"insert into {nameof(RevisionedObject)} (Uid, RevisionType, RevisionUid, RevisionName, RevisionDate, RevisionCreated, RevisionData) values (@0, @1, @2, @3, @4, @5, @6)",
                    obj.Uid,
                    obj.RevisionType,
                    obj.RevisionUid,
                    obj.RevisionName,
                    obj.RevisionDate,
                    obj.RevisionCreated,
                    obj.RevisionData);
            }
            catch (Exception)
            {
            }
        }
    }

    
    private static void MigrateDbLogs(NPoco.Database source, NPoco.Database dest)
    {
        if (source.DatabaseType == DatabaseType.SQLite || dest.DatabaseType == DatabaseType.SQLite)
            return;
        
        var dbLogMessages = source.Fetch<DbLogMessage>($"select * from {nameof(DbLogMessage)}")?.ToArray();
        if (dbLogMessages?.Any() != true)
            return;

        foreach (var obj in dbLogMessages)
        {
            try
            {
                dest.Execute(
                    $"insert into {nameof(DbLogMessage)} (ClientUid, LogDate, Type, Message) values (@0, @1, @2, @3)",
                    obj.ClientUid,
                    obj.LogDate,
                    (int)obj.Type,
                    obj.Message ?? string.Empty);
            }
            catch (Exception)
            {
            }
        }
    }
    
    /// <summary>
    /// Gets a Database from its connection string
    /// </summary>
    /// <param name="connectionString">the connection string</param>
    /// <returns></returns>
    private static NPoco.Database GetDatabase(string connectionString)
    {
        if(string.IsNullOrWhiteSpace(connectionString))
            connectionString = DbManager.GetDefaultConnectionString();
        
        if(connectionString.Contains(".sqlite"))
            return SqliteDbManager.GetDb(connectionString);
        
        if(connectionString.Contains(";Uid="))
            return new NPoco.Database(connectionString,
                null,
                MySqlConnector.MySqlConnectorFactory.Instance);
        
        return new NPoco.Database(connectionString, null, SqlClientFactory.Instance);
    }
}