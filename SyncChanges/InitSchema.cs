using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using NLog;
using NPoco;

namespace DbSync
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
                Array.Sort<int>(_config.Init);

                foreach (var i in _config.Init)
                {
                    switch (i)
                    {
                        case 1:
                            EnableTrackingChangesDb(sourceDb);
                            foreach (var table in _config.ReplicationSets[0].Tables)
                            {
                                EnableTrackingTable(sourceDb, table);
                            }

                            break;
                        case 2:
                            foreach (var table in _config.ReplicationSets[0].Tables)
                            {
                                EnsureTableExists(sourceDb, destinationDb, table);
                            }

                            break;
                        case 3:
                            CreateTableSyncInfo(sourceDb, destinationDb);
                            break;
                        case 4:
                            CopyData(sourceDb.ConnectionString, destinationDb.ConnectionString,
                                _config.ReplicationSets[0].TableColumns);
                            break;
                    }
                }


                Log.Info("Init and copy data completed");
            }
        }


        //1
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

        //1
        private void EnableTrackingTable(Database db, string tableName)
        {
            Log.Info($"Enable tracking for table {tableName}");
            var queryEnableTrackingTable = $@"alter table {tableName}
enable change_tracking
with (track_columns_updated = off)";
            db.Execute(queryEnableTrackingTable);
        }

        //2
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

        //2
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

        //3
        private void CreateTableSyncInfo(Database dbSource, Database dbDestination)
        {
            var queryVersion = "select CHANGE_TRACKING_CURRENT_VERSION()";
            var currentVersion = dbSource.ExecuteScalar<int?>(queryVersion);


            if (currentVersion == null) return;
            var query = $@"CREATE TABLE [dbo].[SyncInfo] (
                                  [Id] int DEFAULT 1 NOT NULL PRIMARY KEY,
                                  [Version] bigint  NOT NULL
                                )";
            dbDestination.Execute(query);
            dbDestination.Execute(@$"INSERT INTO SyncInfo VALUES(1,{currentVersion})");
        }

        //4
        private void CopyData(string sourceConnectionString, string destinationConnectionString,
            List<TableColumns> tables)
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
                    Log.Info($"Copy data from table {table.TableName}");
                    table.Keys.AddRange(table.Columns);
                    var keys = string.Join(",", table.Keys);


                    using (var command = new SqlCommand($"SELECT {keys} FROM {table.TableName}", sourceConnection))
                    using (var reader = command.ExecuteReader())
                    {
                        // Sử dụng SqlBulkCopy để chèn dữ liệu vào bảng đích
                        using (var bulkCopy = new SqlBulkCopy(destinationConnection))
                        {
                            bulkCopy.DestinationTableName = table.TableName;
                            foreach (var key in table.Keys)
                            {
                                bulkCopy.ColumnMappings.Add(key, key);
                            }
                          
                            bulkCopy.WriteToServer(reader);
                        }
                    }
                }
            }

            Log.Info($"Copy data from source to destination completed");
        }
    }
}