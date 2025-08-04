using System.Collections.Generic;

namespace DatabaseComparison.Models
{
    public class TableMetadata
    {
        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public List<ColumnMetadata> Columns { get; set; } = new();
        public List<IndexMetadata> Indexes { get; set; } = new();
        public List<ForeignKeyMetadata> ForeignKeys { get; set; } = new();
        public string? PrimaryKeyName { get; set; }
        public List<string> PrimaryKeyColumns { get; set; } = new();
        public TableStatistics Statistics { get; set; } = new();
    }

    public class ColumnMetadata
    {
        public string ColumnName { get; set; } = string.Empty;
        public int OrdinalPosition { get; set; }
        public string DataType { get; set; } = string.Empty;
        public int? MaxLength { get; set; }
        public int? NumericPrecision { get; set; }
        public int? NumericScale { get; set; }
        public bool IsNullable { get; set; }
        public string? DefaultValue { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsComputed { get; set; }
        public string? ComputedDefinition { get; set; }
    }

    public class IndexMetadata
    {
        public string IndexName { get; set; } = string.Empty;
        public string IndexType { get; set; } = string.Empty;
        public bool IsUnique { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsClustered { get; set; }
        public List<IndexColumnMetadata> Columns { get; set; } = new();
        public string? FilterDefinition { get; set; }
    }

    public class IndexColumnMetadata
    {
        public string ColumnName { get; set; } = string.Empty;
        public int OrdinalPosition { get; set; }
        public bool IsDescending { get; set; }
        public bool IsIncluded { get; set; }
    }

    public class ForeignKeyMetadata
    {
        public string ForeignKeyName { get; set; } = string.Empty;
        public string ReferencedSchema { get; set; } = string.Empty;
        public string ReferencedTable { get; set; } = string.Empty;
        public List<ForeignKeyColumnMetadata> Columns { get; set; } = new();
        public string OnDeleteAction { get; set; } = string.Empty;
        public string OnUpdateAction { get; set; } = string.Empty;
    }

    public class ForeignKeyColumnMetadata
    {
        public string ColumnName { get; set; } = string.Empty;
        public string ReferencedColumnName { get; set; } = string.Empty;
        public int OrdinalPosition { get; set; }
    }
}