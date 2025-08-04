using Microsoft.EntityFrameworkCore;
using DatabaseComparison.Models;
using DatabaseComparison.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DatabaseComparison.Services
{
    public enum ComparisonMethod
    {
        EntityFramework,
        RawSql,
        Both
    }

    public class DatabaseComparisonService
    {
        private readonly EFTableComparisonService? _efService;
        private readonly SqlTableComparisonService? _sqlService;
        private readonly ComparisonMethod _defaultMethod;

        public DatabaseComparisonService(
            DbContext? sourceContext = null, 
            DbContext? targetContext = null,
            string? sourceConnectionString = null, 
            string? targetConnectionString = null,
            ComparisonMethod defaultMethod = ComparisonMethod.RawSql)
        {
            _defaultMethod = defaultMethod;

            if (sourceContext != null && targetContext != null)
            {
                _efService = new EFTableComparisonService(sourceContext, targetContext);
            }

            if (!string.IsNullOrEmpty(sourceConnectionString) && !string.IsNullOrEmpty(targetConnectionString))
            {
                _sqlService = new SqlTableComparisonService(sourceConnectionString, targetConnectionString);
            }

            if (_efService == null && _sqlService == null)
            {
                throw new ArgumentException("Either Entity Framework contexts or connection strings must be provided.");
            }
        }

        public async Task<DatabaseComparisonReport> CompareAllTablesAsync(ComparisonMethod? method = null)
        {
            var comparisonMethod = method ?? _defaultMethod;
            var report = new DatabaseComparisonReport
            {
                ComparisonDate = DateTime.UtcNow,
                ComparisonMethod = comparisonMethod.ToString()
            };

            try
            {
                switch (comparisonMethod)
                {
                    case ComparisonMethod.EntityFramework:
                        if (_efService == null)
                            throw new InvalidOperationException("Entity Framework service not configured.");
                        report.TableResults = await _efService.CompareAllTablesAsync();
                        break;

                    case ComparisonMethod.RawSql:
                        if (_sqlService == null)
                            throw new InvalidOperationException("SQL service not configured.");
                        report.TableResults = await _sqlService.CompareAllTablesAsync();
                        break;

                    case ComparisonMethod.Both:
                        if (_efService == null || _sqlService == null)
                            throw new InvalidOperationException("Both EF and SQL services must be configured for combined comparison.");
                        
                        var efResults = await _efService.CompareAllTablesAsync();
                        var sqlResults = await _sqlService.CompareAllTablesAsync();
                        report.TableResults = CombineResults(efResults, sqlResults);
                        break;
                }

                GenerateReportSummary(report);
            }
            catch (Exception ex)
            {
                report.ErrorMessage = $"Error during comparison: {ex.Message}";
            }

            return report;
        }

        public async Task<TableComparisonResult> CompareTableAsync(string tableName, ComparisonMethod? method = null)
        {
            var comparisonMethod = method ?? _defaultMethod;

            try
            {
                switch (comparisonMethod)
                {
                    case ComparisonMethod.EntityFramework:
                        if (_efService == null)
                            throw new InvalidOperationException("Entity Framework service not configured.");
                        return await _efService.CompareTableAsync(tableName);

                    case ComparisonMethod.RawSql:
                        if (_sqlService == null)
                            throw new InvalidOperationException("SQL service not configured.");
                        return await _sqlService.CompareTableAsync(tableName);

                    case ComparisonMethod.Both:
                        if (_efService == null || _sqlService == null)
                            throw new InvalidOperationException("Both EF and SQL services must be configured for combined comparison.");
                        
                        var efResult = await _efService.CompareTableAsync(tableName);
                        var sqlResult = await _sqlService.CompareTableAsync(tableName);
                        return CombineTableResults(efResult, sqlResult);

                    default:
                        throw new ArgumentException($"Unsupported comparison method: {comparisonMethod}");
                }
            }
            catch (Exception ex)
            {
                return new TableComparisonResult
                {
                    TableName = tableName,
                    Summary = $"Error during comparison: {ex.Message}",
                    ComparisonDate = DateTime.UtcNow
                };
            }
        }

        public async Task<List<string>> GetTableNamesAsync(ComparisonMethod? method = null)
        {
            var comparisonMethod = method ?? _defaultMethod;

            try
            {
                switch (comparisonMethod)
                {
                    case ComparisonMethod.EntityFramework:
                        if (_efService == null)
                            throw new InvalidOperationException("Entity Framework service not configured.");
                        // For EF, we'd need to extract table names from the comparison results
                        var efResults = await _efService.CompareAllTablesAsync();
                        return efResults.Select(r => r.TableName).Distinct().ToList();

                    case ComparisonMethod.RawSql:
                        if (_sqlService == null)
                            throw new InvalidOperationException("SQL service not configured.");
                        var sqlResults = await _sqlService.CompareAllTablesAsync();
                        return sqlResults.Select(r => r.TableName).Distinct().ToList();

                    case ComparisonMethod.Both:
                        if (_efService == null || _sqlService == null)
                            throw new InvalidOperationException("Both EF and SQL services must be configured.");
                        
                        var bothResults = await CompareAllTablesAsync(ComparisonMethod.Both);
                        return bothResults.TableResults.Select(r => r.TableName).Distinct().ToList();

                    default:
                        throw new ArgumentException($"Unsupported comparison method: {comparisonMethod}");
                }
            }
            catch
            {
                return new List<string>();
            }
        }

        public ComparisonCapabilities GetCapabilities()
        {
            return new ComparisonCapabilities
            {
                SupportsEntityFramework = _efService != null,
                SupportsRawSql = _sqlService != null,
                SupportsBothMethods = _efService != null && _sqlService != null,
                SupportsStructuralComparison = true,
                SupportsDataComparison = _sqlService != null, // Data comparison is primarily available through SQL
                SupportsIndexComparison = true,
                SupportsForeignKeyComparison = _sqlService != null
            };
        }

        private List<TableComparisonResult> CombineResults(List<TableComparisonResult> efResults, List<TableComparisonResult> sqlResults)
        {
            var combined = new List<TableComparisonResult>();
            var efResultsDict = efResults.ToDictionary(r => r.TableName, r => r);
            var sqlResultsDict = sqlResults.ToDictionary(r => r.TableName, r => r);

            var allTableNames = efResultsDict.Keys.Union(sqlResultsDict.Keys);

            foreach (var tableName in allTableNames)
            {
                var efResult = efResultsDict.GetValueOrDefault(tableName);
                var sqlResult = sqlResultsDict.GetValueOrDefault(tableName);

                if (efResult != null && sqlResult != null)
                {
                    combined.Add(CombineTableResults(efResult, sqlResult));
                }
                else if (efResult != null)
                {
                    var result = efResult;
                    result.Summary += " (EF comparison only)";
                    combined.Add(result);
                }
                else if (sqlResult != null)
                {
                    var result = sqlResult;
                    result.Summary += " (SQL comparison only)";
                    combined.Add(result);
                }
            }

            return combined;
        }

        private TableComparisonResult CombineTableResults(TableComparisonResult efResult, TableComparisonResult sqlResult)
        {
            var combined = new TableComparisonResult
            {
                TableName = efResult.TableName,
                SchemaName = sqlResult.SchemaName, // SQL typically has more accurate schema info
                ComparisonDate = DateTime.UtcNow,
                SourceStatistics = sqlResult.SourceStatistics, // SQL has more accurate statistics
                TargetStatistics = sqlResult.TargetStatistics
            };

            // Combine column differences (prefer SQL results for accuracy, but include EF insights)
            var combinedColumnDiffs = new List<ColumnDifference>();
            var sqlColumnDiffsDict = sqlResult.ColumnDifferences.ToDictionary(c => c.ColumnName, c => c);

            foreach (var efDiff in efResult.ColumnDifferences)
            {
                if (sqlColumnDiffsDict.TryGetValue(efDiff.ColumnName, out var sqlDiff))
                {
                    // Use SQL difference but add EF insights to description
                    sqlDiff.Description += $" (EF: {efDiff.Description})";
                    combinedColumnDiffs.Add(sqlDiff);
                    sqlColumnDiffsDict.Remove(efDiff.ColumnName);
                }
                else
                {
                    // EF found something SQL didn't
                    efDiff.Description += " (EF only)";
                    combinedColumnDiffs.Add(efDiff);
                }
            }

            // Add remaining SQL-only differences
            combinedColumnDiffs.AddRange(sqlColumnDiffsDict.Values);
            combined.ColumnDifferences = combinedColumnDiffs;

            // Combine index differences (similar approach)
            var combinedIndexDiffs = new List<IndexDifference>();
            var sqlIndexDiffsDict = sqlResult.IndexDifferences.ToDictionary(i => i.IndexName, i => i);

            foreach (var efDiff in efResult.IndexDifferences)
            {
                if (sqlIndexDiffsDict.TryGetValue(efDiff.IndexName, out var sqlDiff))
                {
                    sqlDiff.Description += $" (EF: {efDiff.Description})";
                    combinedIndexDiffs.Add(sqlDiff);
                    sqlIndexDiffsDict.Remove(efDiff.IndexName);
                }
                else
                {
                    efDiff.Description += " (EF only)";
                    combinedIndexDiffs.Add(efDiff);
                }
            }

            combinedIndexDiffs.AddRange(sqlIndexDiffsDict.Values);
            combined.IndexDifferences = combinedIndexDiffs;

            // Use SQL data differences (EF doesn't typically compare data)
            combined.DataDifferences = sqlResult.DataDifferences;

            // Determine combined status
            var hasStructuralDifferences = combined.ColumnDifferences.Any() || combined.IndexDifferences.Any();
            var hasDataDifferences = combined.DataDifferences.Any();

            if (hasStructuralDifferences && hasDataDifferences)
                combined.Status = ComparisonStatus.BothDifferences;
            else if (hasStructuralDifferences)
                combined.Status = ComparisonStatus.StructuralDifferences;
            else if (hasDataDifferences)
                combined.Status = ComparisonStatus.DataDifferences;
            else
                combined.Status = ComparisonStatus.Identical;

            // Generate combined summary
            var summaryParts = new List<string>();
            if (combined.ColumnDifferences.Any())
                summaryParts.Add($"{combined.ColumnDifferences.Count} column difference(s)");
            if (combined.IndexDifferences.Any())
                summaryParts.Add($"{combined.IndexDifferences.Count} index difference(s)");
            if (combined.DataDifferences.Any())
                summaryParts.Add($"{combined.DataDifferences.Count} data difference(s)");

            combined.Summary = summaryParts.Any() 
                ? string.Join(", ", summaryParts) + " (Combined EF + SQL)"
                : "Tables are identical (Combined EF + SQL)";

            return combined;
        }

        private void GenerateReportSummary(DatabaseComparisonReport report)
        {
            if (report.TableResults == null || !report.TableResults.Any())
            {
                report.Summary = "No tables found to compare";
                return;
            }

            var totalTables = report.TableResults.Count;
            var identicalTables = report.TableResults.Count(r => r.Status == ComparisonStatus.Identical);
            var tablesWithStructuralDiffs = report.TableResults.Count(r => 
                r.Status == ComparisonStatus.StructuralDifferences || r.Status == ComparisonStatus.BothDifferences);
            var tablesWithDataDiffs = report.TableResults.Count(r => 
                r.Status == ComparisonStatus.DataDifferences || r.Status == ComparisonStatus.BothDifferences);
            var missingTables = report.TableResults.Count(r => 
                r.Status == ComparisonStatus.MissingInSource || r.Status == ComparisonStatus.MissingInTarget);

            var summaryParts = new List<string>
            {
                $"Total tables: {totalTables}",
                $"Identical: {identicalTables}"
            };

            if (tablesWithStructuralDiffs > 0)
                summaryParts.Add($"Structural differences: {tablesWithStructuralDiffs}");
            
            if (tablesWithDataDiffs > 0)
                summaryParts.Add($"Data differences: {tablesWithDataDiffs}");
            
            if (missingTables > 0)
                summaryParts.Add($"Missing tables: {missingTables}");

            report.Summary = string.Join(", ", summaryParts);

            // Calculate statistics
            report.TotalTables = totalTables;
            report.IdenticalTables = identicalTables;
            report.TablesWithDifferences = totalTables - identicalTables;
            report.TotalColumnDifferences = report.TableResults.Sum(r => r.ColumnDifferences.Count);
            report.TotalIndexDifferences = report.TableResults.Sum(r => r.IndexDifferences.Count);
            report.TotalDataDifferences = report.TableResults.Sum(r => r.DataDifferences.Count);
        }
    }

    public class DatabaseComparisonReport
    {
        public DateTime ComparisonDate { get; set; } = DateTime.UtcNow;
        public string ComparisonMethod { get; set; } = string.Empty;
        public List<TableComparisonResult> TableResults { get; set; } = new();
        public string Summary { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        
        // Statistics
        public int TotalTables { get; set; }
        public int IdenticalTables { get; set; }
        public int TablesWithDifferences { get; set; }
        public int TotalColumnDifferences { get; set; }
        public int TotalIndexDifferences { get; set; }
        public int TotalDataDifferences { get; set; }
    }

    public class ComparisonCapabilities
    {
        public bool SupportsEntityFramework { get; set; }
        public bool SupportsRawSql { get; set; }
        public bool SupportsBothMethods { get; set; }
        public bool SupportsStructuralComparison { get; set; }
        public bool SupportsDataComparison { get; set; }
        public bool SupportsIndexComparison { get; set; }
        public bool SupportsForeignKeyComparison { get; set; }
    }
}