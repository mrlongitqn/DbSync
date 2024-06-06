using NPoco;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using NLog.LayoutRenderers;
using System.Xml.Linq;
using NLog;

namespace SyncChanges
{
    public class InitSchema
    {
        private Config _config;
        static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public InitSchema(Config config)
        {
            _config = config;
        }

        public void Init()
        {
            using (var sourceDb = new Database(_config.ReplicationSets[0].Source.ConnectionString,
                       DatabaseType.SqlServer2005,
                       System.Data.SqlClient.SqlClientFactory.Instance))
            using (var destinationDb = new Database(_config.ReplicationSets[0].Destinations[0].ConnectionString,
                       DatabaseType.SqlServer2005,
                       System.Data.SqlClient.SqlClientFactory.Instance))
            {
                Log.Info("Init structure for tracking changes and sync database");
                EnableTrackingChangesDb(sourceDb);

                foreach (var table in _config.ReplicationSets[0].Tables)
                {
                    EnableTrackingTable(sourceDb, table);
                }

                foreach (var table in _config.ReplicationSets[0].Tables)
                {
                    EnsureTableExists(sourceDb, destinationDb, table);
                }

                CopyData(sourceDb.ConnectionString, destinationDb.ConnectionString, _config.ReplicationSets[0].Tables);
                Log.Info("Init and copy data completed");
            }
        }

        private void EnableTrackingChangesDb(Database db)
        {

            Log.Info("Starting enable tracking for database source");
            var dbSourceName = db.ExecuteScalar<string>("SELECT DB_NAME()");
            var enableTrackingQuery = @$"alter database {dbSourceName}
set change_tracking = on
(change_retention = 2 days, auto_cleanup = on)";
            db.Execute(enableTrackingQuery);
            Log.Info("Enable tracking for database source completed");
        }

        private void EnableTrackingTable(Database db, string tableName)
        {
            Log.Info($"Enable tracking for table {tableName}");
            var queryEnableTrackingTable = $@"alter table {tableName}
enable change_tracking
with (track_columns_updated = off)";
            db.Execute(queryEnableTrackingTable);
        }

        private void EnsureTableExists(Database sourceConnection, Database destinationConnection, string table)
        {
            Log.Info($"Check/Create table {table} for destination database");
            var tableExistsQuery = $@"
        IF NOT EXISTS (SELECT * FROM information_schema.tables WHERE table_name = '{table}')
        BEGIN
            {GetCreateTableScript(sourceConnection, table)}
        END";

            destinationConnection.Execute(tableExistsQuery);
        }

        private string GetCreateTableScript(Database sourceDb, string table)
        {
            var columns = sourceDb.Fetch<dynamic>($@"
    SELECT 
        c.COLUMN_NAME, 
        c.DATA_TYPE, 
        c.CHARACTER_MAXIMUM_LENGTH, 
        c.NUMERIC_PRECISION, 
        c.NUMERIC_SCALE,
        CASE WHEN COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') = 1 THEN 'IDENTITY(1,1)' ELSE '' END AS IS_IDENTITY
    FROM INFORMATION_SCHEMA.COLUMNS c
    WHERE c.TABLE_NAME = @0", table);

            var primaryKeys = sourceDb.Fetch<string>($@"
    SELECT k.COLUMN_NAME
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS t
    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
    ON t.CONSTRAINT_NAME = k.CONSTRAINT_NAME
    WHERE t.TABLE_NAME = @0 AND t.CONSTRAINT_TYPE = 'PRIMARY KEY'", table);

            string createTableScript = $"CREATE TABLE {table} (";

            foreach (var column in columns)
            {
                string nullable = primaryKeys.Contains((string)column.COLUMN_NAME) ? "NOT NULL" : "NULL";
                createTableScript += $@"
        [{column.COLUMN_NAME}] {column.DATA_TYPE}";

                if (column.DATA_TYPE == "nvarchar" || column.DATA_TYPE == "varchar")
                {
                    createTableScript += $"({column.CHARACTER_MAXIMUM_LENGTH})";
                }
                else if (column.DATA_TYPE == "decimal" || column.DATA_TYPE == "numeric")
                {
                    createTableScript += $"({column.NUMERIC_PRECISION}, {column.NUMERIC_SCALE})";
                }

                createTableScript += $" {nullable} {column.IS_IDENTITY},";
            }

            if (primaryKeys.Any())
            {
                createTableScript += " PRIMARY KEY (";
                createTableScript += string.Join(", ", primaryKeys.Select(pk => $"[{pk}]"));
                createTableScript += ")";
            }

            // Remove last comma if there is no primary key or else remove the trailing comma before PRIMARY KEY clause
            createTableScript = createTableScript.TrimEnd(',');
            if (!primaryKeys.Any())
            {
                createTableScript += ");";
            }
            else
            {
                createTableScript += ");";
            }

            return createTableScript;
        }


        private void CopyData(string sourceConnectionString, string destinationConnectionString, List<string> tables)
        {
            Log.Info($"Starting copy data from source to destination");
            using (var sourceConnection = new SqlConnection(sourceConnectionString))
            using (var destinationConnection = new SqlConnection(destinationConnectionString))
            {
                sourceConnection.Open();
                destinationConnection.Open();

                // Lấy dữ liệu từ bảng nguồn
                foreach (var table in tables)
                {
                    Log.Info($"Copy data from table {table}");
                    using (var command = new SqlCommand($"SELECT * FROM {table}", sourceConnection))
                    using (var reader = command.ExecuteReader())
                    {
                        // Sử dụng SqlBulkCopy để chèn dữ liệu vào bảng đích
                        using (var bulkCopy = new SqlBulkCopy(destinationConnection))
                        {
                            bulkCopy.DestinationTableName = table;
                            bulkCopy.WriteToServer(reader);
                        }
                    }
                }
            }
            Log.Info($"Copy data from source to destination completed");
        }
    }
}