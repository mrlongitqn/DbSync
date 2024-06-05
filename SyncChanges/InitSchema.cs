using NPoco;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SyncChanges
{
    public class InitSchema
    {
        private Config _config;
        public InitSchema(Config config)
        {
            _config = config;
            
        }

        public void Init()
        {
            using (var sourceDb = new Database(_config.ReplicationSets[0].Source.ConnectionString, DatabaseType.SqlServer2005,
                       System.Data.SqlClient.SqlClientFactory.Instance))
            using (var destinationDb = new Database(_config.ReplicationSets[0].Destinations[0].ConnectionString, DatabaseType.SqlServer2005,
                       System.Data.SqlClient.SqlClientFactory.Instance))
            {
                var tables = sourceDb.Fetch<string>("SELECT table_name FROM information_schema.tables WHERE table_type = 'BASE TABLE'");

                foreach (var table in _config.ReplicationSets[0].Tables)
                {
                    EnsureTableExists(sourceDb, destinationDb, table);
                    // RemoveForeignKeysAndAllowNulls(destinationDb, table);
                    // CopyData(sourceDb, destinationDb, table);
                }
            }
        }
        private void EnsureTableExists(Database sourceConnection, Database destinationConnection, string table)
        {
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

    }
}
