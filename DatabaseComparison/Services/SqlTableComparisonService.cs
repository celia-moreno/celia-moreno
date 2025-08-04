using System.Data;
using System.Data.SqlClient;
using DatabaseComparison.Models;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DatabaseComparison.Services
{
    public class SqlTableComparisonService
    {
        private readonly string _sourceConnectionString;
        private readonly string _targetConnectionString;

        public SqlTableComparisonService(string sourceConnectionString, string targetConnectionString)
        {
            _sourceConnectionString = sourceConnectionString;
            _targetConnectionString = targetConnectionString;
        }

        public async Task<List<TableComparisonResult>> CompareAllTablesAsync()
        {
            var results = new List<TableComparisonResult>();

            var sourceTableNames = await GetTableNamesAsync(_sourceConnectionString);
            var targetTableNames = await GetTableNamesAsync(_targetConnectionString);

            var allTableNames = sourceTableNames.Union(targetTableNames).Distinct();

            foreach (var tableName in allTableNames)
            {
                var result = await CompareTableAsync(tableName);
                results.Add(result);
            }

            return results;
        }

        public async Task<TableComparisonResult> CompareTableAsync(string tableName)
        {
            var result = new TableComparisonResult
            {
                TableName = tableName,
                ComparisonDate = DateTime.UtcNow
            };

            try
            {
                var sourceMetadata = await GetTableMetadataAsync(_sourceConnectionString, tableName);
                var targetMetadata = await GetTableMetadataAsync(_targetConnectionString, tableName);

                if (sourceMetadata == null && targetMetadata == null)
                {
                    result.Status = ComparisonStatus.MissingInTarget;
                    result.Summary = "Table not found in either database";
                    return result;
                }

                if (sourceMetadata == null)
                {
                    result.Status = ComparisonStatus.MissingInSource;
                    result.Summary = "Table exists only in target database";
                    result.TargetStatistics = targetMetadata?.Statistics ?? new TableStatistics();
                    return result;
                }

                if (targetMetadata == null)
                {
                    result.Status = ComparisonStatus.MissingInTarget;
                    result.Summary = "Table exists only in source database";
                    result.SourceStatistics = sourceMetadata.Statistics;
                    return result;
                }

                result.SchemaName = sourceMetadata.SchemaName;
                result.SourceStatistics = sourceMetadata.Statistics;
                result.TargetStatistics = targetMetadata.Statistics;

                // Compare columns
                CompareColumns(sourceMetadata, targetMetadata, result);

                // Compare indexes
                CompareIndexes(sourceMetadata, targetMetadata, result);

                // Compare sample data if requested
                await CompareDataAsync(_sourceConnectionString, _targetConnectionString, sourceMetadata, result);

                // Determine overall status
                DetermineComparisonStatus(result);

                // Generate summary
                GenerateSummary(result);
            }
            catch (Exception ex)
            {
                result.Summary = $"Error during comparison: {ex.Message}";
            }

            return result;
        }

        private async Task<List<string>> GetTableNamesAsync(string connectionString)
        {
            var tableNames = new List<string>();

            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT TABLE_NAME 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_TYPE = 'BASE TABLE' 
                ORDER BY TABLE_NAME";

            using var command = new SqlCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tableNames.Add(reader.GetString("TABLE_NAME"));
            }

            return tableNames;
        }

        private async Task<TableMetadata?> GetTableMetadataAsync(string connectionString, string tableName)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var metadata = new TableMetadata { TableName = tableName };

                // Get basic table info
                var tableInfoSql = @"
                    SELECT TABLE_SCHEMA, TABLE_NAME 
                    FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_NAME = @TableName AND TABLE_TYPE = 'BASE TABLE'";

                using (var command = new SqlCommand(tableInfoSql, connection))
                {
                    command.Parameters.AddWithValue("@TableName", tableName);
                    using var reader = await command.ExecuteReaderAsync();

                    if (!await reader.ReadAsync())
                        return null;

                    metadata.SchemaName = reader.GetString("TABLE_SCHEMA");
                    metadata.TableName = reader.GetString("TABLE_NAME");
                }

                // Get column metadata
                metadata.Columns = await GetColumnMetadataAsync(connection, metadata.SchemaName, metadata.TableName);

                // Get index metadata
                metadata.Indexes = await GetIndexMetadataAsync(connection, metadata.SchemaName, metadata.TableName);

                // Get primary key information
                await GetPrimaryKeyInfoAsync(connection, metadata);

                // Get foreign key metadata
                metadata.ForeignKeys = await GetForeignKeyMetadataAsync(connection, metadata.SchemaName, metadata.TableName);

                // Get table statistics
                metadata.Statistics = await GetTableStatisticsAsync(connection, metadata.SchemaName, metadata.TableName);

                return metadata;
            }
            catch
            {
                return null;
            }
        }

        private async Task<List<ColumnMetadata>> GetColumnMetadataAsync(SqlConnection connection, string schemaName, string tableName)
        {
            var columns = new List<ColumnMetadata>();

            var sql = @"
                SELECT 
                    c.COLUMN_NAME,
                    c.ORDINAL_POSITION,
                    c.DATA_TYPE,
                    c.CHARACTER_MAXIMUM_LENGTH,
                    c.NUMERIC_PRECISION,
                    c.NUMERIC_SCALE,
                    c.IS_NULLABLE,
                    c.COLUMN_DEFAULT,
                    COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') AS IsIdentity,
                    COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsComputed') AS IsComputed,
                    cc.definition AS ComputedDefinition
                FROM INFORMATION_SCHEMA.COLUMNS c
                LEFT JOIN sys.computed_columns cc ON cc.object_id = OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME) 
                    AND cc.name = c.COLUMN_NAME
                WHERE c.TABLE_SCHEMA = @SchemaName AND c.TABLE_NAME = @TableName
                ORDER BY c.ORDINAL_POSITION";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@SchemaName", schemaName);
            command.Parameters.AddWithValue("@TableName", tableName);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var column = new ColumnMetadata
                {
                    ColumnName = reader.GetString("COLUMN_NAME"),
                    OrdinalPosition = reader.GetInt32("ORDINAL_POSITION"),
                    DataType = reader.GetString("DATA_TYPE"),
                    MaxLength = reader.IsDBNull("CHARACTER_MAXIMUM_LENGTH") ? null : reader.GetInt32("CHARACTER_MAXIMUM_LENGTH"),
                    NumericPrecision = reader.IsDBNull("NUMERIC_PRECISION") ? null : (int?)reader.GetByte("NUMERIC_PRECISION"),
                    NumericScale = reader.IsDBNull("NUMERIC_SCALE") ? null : reader.GetInt32("NUMERIC_SCALE"),
                    IsNullable = reader.GetString("IS_NULLABLE") == "YES",
                    DefaultValue = reader.IsDBNull("COLUMN_DEFAULT") ? null : reader.GetString("COLUMN_DEFAULT"),
                    IsIdentity = reader.GetInt32("IsIdentity") == 1,
                    IsComputed = reader.GetInt32("IsComputed") == 1,
                    ComputedDefinition = reader.IsDBNull("ComputedDefinition") ? null : reader.GetString("ComputedDefinition")
                };

                columns.Add(column);
            }

            return columns;
        }

        private async Task<List<IndexMetadata>> GetIndexMetadataAsync(SqlConnection connection, string schemaName, string tableName)
        {
            var indexes = new List<IndexMetadata>();

            var sql = @"
                SELECT DISTINCT
                    i.name AS IndexName,
                    i.type_desc AS IndexType,
                    i.is_unique AS IsUnique,
                    i.is_primary_key AS IsPrimaryKey,
                    CASE WHEN i.type = 1 THEN 1 ELSE 0 END AS IsClustered,
                    i.filter_definition AS FilterDefinition
                FROM sys.indexes i
                INNER JOIN sys.objects o ON i.object_id = o.object_id
                INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                WHERE s.name = @SchemaName AND o.name = @TableName AND i.type > 0
                ORDER BY i.name";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@SchemaName", schemaName);
            command.Parameters.AddWithValue("@TableName", tableName);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var index = new IndexMetadata
                {
                    IndexName = reader.GetString("IndexName"),
                    IndexType = reader.GetString("IndexType"),
                    IsUnique = reader.GetBoolean("IsUnique"),
                    IsPrimaryKey = reader.GetBoolean("IsPrimaryKey"),
                    IsClustered = reader.GetBoolean("IsClustered"),
                    FilterDefinition = reader.IsDBNull("FilterDefinition") ? null : reader.GetString("FilterDefinition")
                };

                indexes.Add(index);
            }

            // Get column information for each index
            foreach (var index in indexes)
            {
                index.Columns = await GetIndexColumnsAsync(connection, schemaName, tableName, index.IndexName);
            }

            return indexes;
        }

        private async Task<List<IndexColumnMetadata>> GetIndexColumnsAsync(SqlConnection connection, string schemaName, string tableName, string indexName)
        {
            var columns = new List<IndexColumnMetadata>();

            var sql = @"
                SELECT 
                    c.name AS ColumnName,
                    ic.key_ordinal AS OrdinalPosition,
                    ic.is_descending_key AS IsDescending,
                    ic.is_included_column AS IsIncluded
                FROM sys.index_columns ic
                INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                INNER JOIN sys.objects o ON i.object_id = o.object_id
                INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                WHERE s.name = @SchemaName AND o.name = @TableName AND i.name = @IndexName
                ORDER BY ic.key_ordinal, ic.index_column_id";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@SchemaName", schemaName);
            command.Parameters.AddWithValue("@TableName", tableName);
            command.Parameters.AddWithValue("@IndexName", indexName);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var column = new IndexColumnMetadata
                {
                    ColumnName = reader.GetString("ColumnName"),
                    OrdinalPosition = reader.GetByte("OrdinalPosition"),
                    IsDescending = reader.GetBoolean("IsDescending"),
                    IsIncluded = reader.GetBoolean("IsIncluded")
                };

                columns.Add(column);
            }

            return columns;
        }

        private async Task GetPrimaryKeyInfoAsync(SqlConnection connection, TableMetadata metadata)
        {
            var sql = @"
                SELECT 
                    kc.CONSTRAINT_NAME,
                    kc.COLUMN_NAME
                FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kc
                INNER JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc 
                    ON kc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' 
                    AND kc.TABLE_SCHEMA = @SchemaName 
                    AND kc.TABLE_NAME = @TableName
                ORDER BY kc.ORDINAL_POSITION";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@SchemaName", metadata.SchemaName);
            command.Parameters.AddWithValue("@TableName", metadata.TableName);

            using var reader = await command.ExecuteReaderAsync();

            var primaryKeyColumns = new List<string>();
            string? primaryKeyName = null;

            while (await reader.ReadAsync())
            {
                primaryKeyName = reader.GetString("CONSTRAINT_NAME");
                primaryKeyColumns.Add(reader.GetString("COLUMN_NAME"));
            }

            metadata.PrimaryKeyName = primaryKeyName;
            metadata.PrimaryKeyColumns = primaryKeyColumns;
        }

        private async Task<List<ForeignKeyMetadata>> GetForeignKeyMetadataAsync(SqlConnection connection, string schemaName, string tableName)
        {
            var foreignKeys = new List<ForeignKeyMetadata>();

            var sql = @"
                SELECT DISTINCT
                    fk.name AS ForeignKeyName,
                    rs.name AS ReferencedSchema,
                    rt.name AS ReferencedTable,
                    fk.delete_referential_action_desc AS OnDeleteAction,
                    fk.update_referential_action_desc AS OnUpdateAction
                FROM sys.foreign_keys fk
                INNER JOIN sys.objects o ON fk.parent_object_id = o.object_id
                INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                INNER JOIN sys.objects rt ON fk.referenced_object_id = rt.object_id
                INNER JOIN sys.schemas rs ON rt.schema_id = rs.schema_id
                WHERE s.name = @SchemaName AND o.name = @TableName";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@SchemaName", schemaName);
            command.Parameters.AddWithValue("@TableName", tableName);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var foreignKey = new ForeignKeyMetadata
                {
                    ForeignKeyName = reader.GetString("ForeignKeyName"),
                    ReferencedSchema = reader.GetString("ReferencedSchema"),
                    ReferencedTable = reader.GetString("ReferencedTable"),
                    OnDeleteAction = reader.GetString("OnDeleteAction"),
                    OnUpdateAction = reader.GetString("OnUpdateAction")
                };

                foreignKeys.Add(foreignKey);
            }

            // Get column information for each foreign key
            foreach (var foreignKey in foreignKeys)
            {
                foreignKey.Columns = await GetForeignKeyColumnsAsync(connection, schemaName, tableName, foreignKey.ForeignKeyName);
            }

            return foreignKeys;
        }

        private async Task<List<ForeignKeyColumnMetadata>> GetForeignKeyColumnsAsync(SqlConnection connection, string schemaName, string tableName, string foreignKeyName)
        {
            var columns = new List<ForeignKeyColumnMetadata>();

            var sql = @"
                SELECT 
                    c.name AS ColumnName,
                    rc.name AS ReferencedColumnName,
                    fkc.constraint_column_id AS OrdinalPosition
                FROM sys.foreign_key_columns fkc
                INNER JOIN sys.foreign_keys fk ON fkc.constraint_object_id = fk.object_id
                INNER JOIN sys.columns c ON fkc.parent_object_id = c.object_id AND fkc.parent_column_id = c.column_id
                INNER JOIN sys.columns rc ON fkc.referenced_object_id = rc.object_id AND fkc.referenced_column_id = rc.column_id
                INNER JOIN sys.objects o ON fk.parent_object_id = o.object_id
                INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                WHERE s.name = @SchemaName AND o.name = @TableName AND fk.name = @ForeignKeyName
                ORDER BY fkc.constraint_column_id";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@SchemaName", schemaName);
            command.Parameters.AddWithValue("@TableName", tableName);
            command.Parameters.AddWithValue("@ForeignKeyName", foreignKeyName);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var column = new ForeignKeyColumnMetadata
                {
                    ColumnName = reader.GetString("ColumnName"),
                    ReferencedColumnName = reader.GetString("ReferencedColumnName"),
                    OrdinalPosition = reader.GetInt32("OrdinalPosition")
                };

                columns.Add(column);
            }

            return columns;
        }

        private async Task<TableStatistics> GetTableStatisticsAsync(SqlConnection connection, string schemaName, string tableName)
        {
            var statistics = new TableStatistics();

            try
            {
                // Get row count
                var rowCountSql = $"SELECT COUNT(*) FROM [{schemaName}].[{tableName}]";
                using (var command = new SqlCommand(rowCountSql, connection))
                {
                    var result = await command.ExecuteScalarAsync();
                    statistics.RowCount = Convert.ToInt64(result);
                }

                // Get additional statistics
                var statsSql = @"
                    SELECT 
                        COUNT(*) AS ColumnCount,
                        (SELECT COUNT(*) FROM sys.indexes i 
                         INNER JOIN sys.objects o ON i.object_id = o.object_id 
                         INNER JOIN sys.schemas s ON o.schema_id = s.schema_id 
                         WHERE s.name = @SchemaName AND o.name = @TableName AND i.type > 0) AS IndexCount,
                        COALESCE((SELECT SUM(a.total_pages) * 8 * 1024 
                                 FROM sys.allocation_units a
                                 INNER JOIN sys.partitions p ON a.container_id = p.hobt_id
                                 INNER JOIN sys.objects o ON p.object_id = o.object_id
                                 INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                                 WHERE s.name = @SchemaName AND o.name = @TableName), 0) AS SizeInBytes,
                        o.modify_date AS LastModified
                    FROM INFORMATION_SCHEMA.COLUMNS c
                    INNER JOIN sys.objects o ON o.name = c.TABLE_NAME
                    INNER JOIN sys.schemas s ON o.schema_id = s.schema_id AND s.name = c.TABLE_SCHEMA
                    WHERE c.TABLE_SCHEMA = @SchemaName AND c.TABLE_NAME = @TableName
                    GROUP BY o.modify_date";

                using (var command = new SqlCommand(statsSql, connection))
                {
                    command.Parameters.AddWithValue("@SchemaName", schemaName);
                    command.Parameters.AddWithValue("@TableName", tableName);

                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        statistics.ColumnCount = reader.GetInt32("ColumnCount");
                        statistics.IndexCount = reader.GetInt32("IndexCount");
                        statistics.SizeInBytes = reader.GetInt64("SizeInBytes");
                        statistics.LastModified = reader.GetDateTime("LastModified");
                    }
                }
            }
            catch
            {
                // If we can't get statistics, return empty stats
            }

            return statistics;
        }

        private async Task CompareDataAsync(string sourceConnectionString, string targetConnectionString, TableMetadata metadata, TableComparisonResult result, int sampleSize = 100)
        {
            if (!metadata.PrimaryKeyColumns.Any())
                return; // Can't compare data without a primary key

            try
            {
                var primaryKeyColumn = metadata.PrimaryKeyColumns.First();
                var sampleSql = $@"
                    SELECT TOP {sampleSize} * 
                    FROM [{metadata.SchemaName}].[{metadata.TableName}] 
                    ORDER BY [{primaryKeyColumn}]";

                var sourceData = new Dictionary<string, Dictionary<string, object?>>();
                var targetData = new Dictionary<string, Dictionary<string, object?>>();

                // Get sample data from source
                using (var sourceConnection = new SqlConnection(sourceConnectionString))
                {
                    await sourceConnection.OpenAsync();
                    using var command = new SqlCommand(sampleSql, sourceConnection);
                    using var reader = await command.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        var pkValue = reader[primaryKeyColumn]?.ToString() ?? string.Empty;
                        var row = new Dictionary<string, object?>();

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }

                        sourceData[pkValue] = row;
                    }
                }

                // Get sample data from target
                using (var targetConnection = new SqlConnection(targetConnectionString))
                {
                    await targetConnection.OpenAsync();
                    using var command = new SqlCommand(sampleSql, targetConnection);
                    using var reader = await command.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        var pkValue = reader[primaryKeyColumn]?.ToString() ?? string.Empty;
                        var row = new Dictionary<string, object?>();

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }

                        targetData[pkValue] = row;
                    }
                }

                // Compare the data
                var allKeys = sourceData.Keys.Union(targetData.Keys);
                foreach (var key in allKeys)
                {
                    var sourceRow = sourceData.GetValueOrDefault(key);
                    var targetRow = targetData.GetValueOrDefault(key);

                    if (sourceRow == null)
                    {
                        result.DataDifferences.Add(new DataDifference
                        {
                            PrimaryKeyValue = key,
                            ColumnName = "ROW",
                            Type = DifferenceType.Missing,
                            TargetValue = "Row exists",
                            Description = "Row exists only in target table"
                        });
                    }
                    else if (targetRow == null)
                    {
                        result.DataDifferences.Add(new DataDifference
                        {
                            PrimaryKeyValue = key,
                            ColumnName = "ROW",
                            Type = DifferenceType.Added,
                            SourceValue = "Row exists",
                            Description = "Row exists only in source table"
                        });
                    }
                    else
                    {
                        // Compare column values
                        foreach (var column in metadata.Columns)
                        {
                            var sourceValue = sourceRow.GetValueOrDefault(column.ColumnName);
                            var targetValue = targetRow.GetValueOrDefault(column.ColumnName);

                            if (!Equals(sourceValue, targetValue))
                            {
                                result.DataDifferences.Add(new DataDifference
                                {
                                    PrimaryKeyValue = key,
                                    ColumnName = column.ColumnName,
                                    Type = DifferenceType.Modified,
                                    SourceValue = sourceValue,
                                    TargetValue = targetValue,
                                    Description = $"Value differs: {sourceValue} vs {targetValue}"
                                });
                            }
                        }
                    }
                }
            }
            catch
            {
                // If data comparison fails, continue without data differences
            }
        }

        private void CompareColumns(TableMetadata source, TableMetadata target, TableComparisonResult result)
        {
            var sourceColumns = source.Columns.ToDictionary(c => c.ColumnName, c => c);
            var targetColumns = target.Columns.ToDictionary(c => c.ColumnName, c => c);

            var allColumnNames = sourceColumns.Keys.Union(targetColumns.Keys);

            foreach (var columnName in allColumnNames)
            {
                var sourceColumn = sourceColumns.GetValueOrDefault(columnName);
                var targetColumn = targetColumns.GetValueOrDefault(columnName);

                if (sourceColumn == null)
                {
                    result.ColumnDifferences.Add(new ColumnDifference
                    {
                        ColumnName = columnName,
                        Type = DifferenceType.Missing,
                        TargetDataType = targetColumn?.DataType ?? string.Empty,
                        TargetIsNullable = targetColumn?.IsNullable ?? false,
                        TargetDefaultValue = targetColumn?.DefaultValue,
                        Description = "Column exists only in target table"
                    });
                }
                else if (targetColumn == null)
                {
                    result.ColumnDifferences.Add(new ColumnDifference
                    {
                        ColumnName = columnName,
                        Type = DifferenceType.Added,
                        SourceDataType = sourceColumn.DataType,
                        SourceIsNullable = sourceColumn.IsNullable,
                        SourceDefaultValue = sourceColumn.DefaultValue,
                        Description = "Column exists only in source table"
                    });
                }
                else
                {
                    CompareColumnProperties(sourceColumn, targetColumn, result);
                }
            }
        }

        private void CompareColumnProperties(ColumnMetadata source, ColumnMetadata target, TableComparisonResult result)
        {
            var differences = new List<string>();

            if (source.DataType != target.DataType)
            {
                differences.Add($"Data type: {source.DataType} vs {target.DataType}");
            }

            if (source.IsNullable != target.IsNullable)
            {
                differences.Add($"Nullability: {source.IsNullable} vs {target.IsNullable}");
            }

            if (source.DefaultValue != target.DefaultValue)
            {
                differences.Add($"Default value: {source.DefaultValue ?? "NULL"} vs {target.DefaultValue ?? "NULL"}");
            }

            if (differences.Any())
            {
                var primaryDifference = DifferenceType.Modified;
                if (source.DataType != target.DataType)
                    primaryDifference = DifferenceType.TypeChanged;
                else if (source.IsNullable != target.IsNullable)
                    primaryDifference = DifferenceType.NullabilityChanged;
                else if (source.DefaultValue != target.DefaultValue)
                    primaryDifference = DifferenceType.DefaultValueChanged;

                result.ColumnDifferences.Add(new ColumnDifference
                {
                    ColumnName = source.ColumnName,
                    Type = primaryDifference,
                    SourceDataType = source.DataType,
                    TargetDataType = target.DataType,
                    SourceIsNullable = source.IsNullable,
                    TargetIsNullable = target.IsNullable,
                    SourceDefaultValue = source.DefaultValue,
                    TargetDefaultValue = target.DefaultValue,
                    Description = string.Join("; ", differences)
                });
            }
        }

        private void CompareIndexes(TableMetadata source, TableMetadata target, TableComparisonResult result)
        {
            var sourceIndexes = source.Indexes.ToDictionary(i => i.IndexName, i => i);
            var targetIndexes = target.Indexes.ToDictionary(i => i.IndexName, i => i);

            var allIndexNames = sourceIndexes.Keys.Union(targetIndexes.Keys);

            foreach (var indexName in allIndexNames)
            {
                var sourceIndex = sourceIndexes.GetValueOrDefault(indexName);
                var targetIndex = targetIndexes.GetValueOrDefault(indexName);

                if (sourceIndex == null)
                {
                    result.IndexDifferences.Add(new IndexDifference
                    {
                        IndexName = indexName,
                        Type = DifferenceType.Missing,
                        IndexType = targetIndex?.IndexType ?? string.Empty,
                        TargetColumns = targetIndex?.Columns.Select(c => c.ColumnName).ToList() ?? new List<string>(),
                        TargetIsUnique = targetIndex?.IsUnique ?? false,
                        Description = "Index exists only in target table"
                    });
                }
                else if (targetIndex == null)
                {
                    result.IndexDifferences.Add(new IndexDifference
                    {
                        IndexName = indexName,
                        Type = DifferenceType.Added,
                        IndexType = sourceIndex.IndexType,
                        SourceColumns = sourceIndex.Columns.Select(c => c.ColumnName).ToList(),
                        SourceIsUnique = sourceIndex.IsUnique,
                        Description = "Index exists only in source table"
                    });
                }
                else
                {
                    CompareIndexProperties(sourceIndex, targetIndex, result);
                }
            }
        }

        private void CompareIndexProperties(IndexMetadata source, IndexMetadata target, TableComparisonResult result)
        {
            var differences = new List<string>();

            if (source.IsUnique != target.IsUnique)
            {
                differences.Add($"Uniqueness: {source.IsUnique} vs {target.IsUnique}");
            }

            var sourceColumns = source.Columns.Select(c => c.ColumnName).OrderBy(c => c).ToList();
            var targetColumns = target.Columns.Select(c => c.ColumnName).OrderBy(c => c).ToList();

            if (!sourceColumns.SequenceEqual(targetColumns))
            {
                differences.Add($"Columns: [{string.Join(", ", sourceColumns)}] vs [{string.Join(", ", targetColumns)}]");
            }

            if (differences.Any())
            {
                result.IndexDifferences.Add(new IndexDifference
                {
                    IndexName = source.IndexName,
                    Type = DifferenceType.Modified,
                    IndexType = source.IndexType,
                    SourceColumns = sourceColumns,
                    TargetColumns = targetColumns,
                    SourceIsUnique = source.IsUnique,
                    TargetIsUnique = target.IsUnique,
                    Description = string.Join("; ", differences)
                });
            }
        }

        private void DetermineComparisonStatus(TableComparisonResult result)
        {
            var hasStructuralDifferences = result.ColumnDifferences.Any() || result.IndexDifferences.Any();
            var hasDataDifferences = result.DataDifferences.Any();

            if (hasStructuralDifferences && hasDataDifferences)
                result.Status = ComparisonStatus.BothDifferences;
            else if (hasStructuralDifferences)
                result.Status = ComparisonStatus.StructuralDifferences;
            else if (hasDataDifferences)
                result.Status = ComparisonStatus.DataDifferences;
            else
                result.Status = ComparisonStatus.Identical;
        }

        private void GenerateSummary(TableComparisonResult result)
        {
            var summaryParts = new List<string>();

            if (result.ColumnDifferences.Any())
            {
                summaryParts.Add($"{result.ColumnDifferences.Count} column difference(s)");
            }

            if (result.IndexDifferences.Any())
            {
                summaryParts.Add($"{result.IndexDifferences.Count} index difference(s)");
            }

            if (result.DataDifferences.Any())
            {
                summaryParts.Add($"{result.DataDifferences.Count} data difference(s)");
            }

            result.Summary = summaryParts.Any() 
                ? string.Join(", ", summaryParts)
                : "Tables are identical";
        }
    }
}