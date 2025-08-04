using System.Collections.Generic;

namespace DatabaseComparison.Models
{
    public class TableComparisonResult
    {
        public string TableName { get; set; } = string.Empty;
        public string SchemaName { get; set; } = string.Empty;
        public ComparisonStatus Status { get; set; }
        public List<ColumnDifference> ColumnDifferences { get; set; } = new();
        public List<IndexDifference> IndexDifferences { get; set; } = new();
        public List<DataDifference> DataDifferences { get; set; } = new();
        public TableStatistics SourceStatistics { get; set; } = new();
        public TableStatistics TargetStatistics { get; set; } = new();
        public string Summary { get; set; } = string.Empty;
        public DateTime ComparisonDate { get; set; } = DateTime.UtcNow;
    }

    public enum ComparisonStatus
    {
        Identical,
        StructuralDifferences,
        DataDifferences,
        BothDifferences,
        MissingInTarget,
        MissingInSource
    }

    public class ColumnDifference
    {
        public string ColumnName { get; set; } = string.Empty;
        public DifferenceType Type { get; set; }
        public string SourceDataType { get; set; } = string.Empty;
        public string TargetDataType { get; set; } = string.Empty;
        public bool SourceIsNullable { get; set; }
        public bool TargetIsNullable { get; set; }
        public string? SourceDefaultValue { get; set; }
        public string? TargetDefaultValue { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public class IndexDifference
    {
        public string IndexName { get; set; } = string.Empty;
        public DifferenceType Type { get; set; }
        public string IndexType { get; set; } = string.Empty;
        public List<string> SourceColumns { get; set; } = new();
        public List<string> TargetColumns { get; set; } = new();
        public bool SourceIsUnique { get; set; }
        public bool TargetIsUnique { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public class DataDifference
    {
        public string PrimaryKeyValue { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public DifferenceType Type { get; set; }
        public object? SourceValue { get; set; }
        public object? TargetValue { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public enum DifferenceType
    {
        Missing,
        Added,
        Modified,
        TypeChanged,
        NullabilityChanged,
        DefaultValueChanged
    }

    public class TableStatistics
    {
        public long RowCount { get; set; }
        public int ColumnCount { get; set; }
        public int IndexCount { get; set; }
        public long SizeInBytes { get; set; }
        public DateTime LastModified { get; set; }
    }
}