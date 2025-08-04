using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using DatabaseComparison.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DatabaseComparison.Services
{
    public class EFTableComparisonService
    {
        private readonly DbContext _sourceContext;
        private readonly DbContext _targetContext;

        public EFTableComparisonService(DbContext sourceContext, DbContext targetContext)
        {
            _sourceContext = sourceContext;
            _targetContext = targetContext;
        }

        public async Task<List<TableComparisonResult>> CompareAllTablesAsync()
        {
            var results = new List<TableComparisonResult>();

            var sourceTableNames = GetTableNames(_sourceContext);
            var targetTableNames = GetTableNames(_targetContext);

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
                var sourceMetadata = await GetTableMetadataAsync(_sourceContext, tableName);
                var targetMetadata = await GetTableMetadataAsync(_targetContext, tableName);

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

        private async Task<TableMetadata?> GetTableMetadataAsync(DbContext context, string tableName)
        {
            try
            {
                var entityType = context.Model.GetEntityTypes()
                    .FirstOrDefault(e => e.GetTableName() == tableName);

                if (entityType == null)
                    return null;

                var metadata = new TableMetadata
                {
                    TableName = entityType.GetTableName() ?? string.Empty,
                    SchemaName = entityType.GetSchema() ?? "dbo"
                };

                // Get column metadata from EF model
                foreach (var property in entityType.GetProperties())
                {
                    var columnMetadata = new ColumnMetadata
                    {
                        ColumnName = property.GetColumnName(),
                        DataType = property.GetColumnType(),
                        IsNullable = property.IsNullable,
                        DefaultValue = property.GetDefaultValueSql(),
                        OrdinalPosition = property.GetColumnOrder() ?? 0
                    };

                    metadata.Columns.Add(columnMetadata);
                }

                // Get primary key information
                var primaryKey = entityType.FindPrimaryKey();
                if (primaryKey != null)
                {
                    metadata.PrimaryKeyName = primaryKey.GetName();
                    metadata.PrimaryKeyColumns = primaryKey.Properties
                        .Select(p => p.GetColumnName())
                        .ToList();
                }

                // Get index information
                foreach (var index in entityType.GetIndexes())
                {
                    var indexMetadata = new IndexMetadata
                    {
                        IndexName = index.GetDatabaseName() ?? string.Empty,
                        IsUnique = index.IsUnique,
                        IsPrimaryKey = index.GetName() == metadata.PrimaryKeyName
                    };

                    foreach (var property in index.Properties)
                    {
                        indexMetadata.Columns.Add(new IndexColumnMetadata
                        {
                            ColumnName = property.GetColumnName(),
                            OrdinalPosition = indexMetadata.Columns.Count + 1
                        });
                    }

                    metadata.Indexes.Add(indexMetadata);
                }

                // Get basic statistics (this would require additional SQL queries for accurate data)
                metadata.Statistics = await GetTableStatisticsAsync(context, metadata.SchemaName, metadata.TableName);

                return metadata;
            }
            catch
            {
                return null;
            }
        }

        private async Task<TableStatistics> GetTableStatisticsAsync(DbContext context, string schemaName, string tableName)
        {
            try
            {
                // Note: This is a simplified version. In practice, you'd use SQL queries to get accurate statistics
                var stats = new TableStatistics
                {
                    LastModified = DateTime.UtcNow
                };

                // You would typically execute SQL commands here to get actual statistics
                // For demonstration, we'll return basic stats
                return stats;
            }
            catch
            {
                return new TableStatistics();
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

        private List<string> GetTableNames(DbContext context)
        {
            return context.Model.GetEntityTypes()
                .Select(e => e.GetTableName())
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList()!;
        }
    }
}