# Database Table Comparison Service

A comprehensive .NET service for comparing database tables using both Entity Framework and raw SQL approaches. This service provides detailed structural and data comparisons between source and target databases.

## Features

### 🔍 **Comparison Methods**
- **Entity Framework-based**: Uses EF Core metadata for model-driven comparisons
- **Raw SQL-based**: Queries system tables for comprehensive database schema analysis
- **Combined**: Leverages both approaches for maximum accuracy and coverage

### 📊 **Comparison Types**
- **Structural Comparison**: Columns, data types, nullability, default values
- **Index Comparison**: Primary keys, unique indexes, clustered/non-clustered indexes
- **Foreign Key Comparison**: Relationships, cascade rules, referenced tables
- **Data Comparison**: Sample data comparison with configurable row limits

### 📈 **Rich Results**
- Detailed difference reports with categorized changes
- Statistics and summaries
- JSON export capabilities
- Status indicators for different types of differences

## Architecture

```
DatabaseComparison/
├── Models/                     # Data models for comparison results
│   ├── TableComparisonResult.cs
│   └── TableMetadata.cs
├── Services/                   # Core comparison services
│   ├── EFTableComparisonService.cs
│   ├── SqlTableComparisonService.cs
│   └── DatabaseComparisonService.cs
├── Sample/                     # Sample implementation
│   └── SampleDbContext.cs
└── Program.cs                  # Demo application
```

## Quick Start

### 1. Installation

Add the required NuGet packages:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="9.0.7" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.7" />
<PackageReference Include="System.Data.SqlClient" Version="4.9.0" />
<PackageReference Include="Microsoft.Extensions.Configuration" Version="9.0.7" />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="9.0.7" />
```

### 2. Basic Usage

#### SQL-based Comparison (Recommended)
```csharp
var service = new DatabaseComparisonService(
    sourceConnectionString: "Server=...;Database=SourceDb;...",
    targetConnectionString: "Server=...;Database=TargetDb;...",
    defaultMethod: ComparisonMethod.RawSql);

var report = await service.CompareAllTablesAsync();
Console.WriteLine($"Found {report.TablesWithDifferences} tables with differences");
```

#### Entity Framework-based Comparison
```csharp
using var sourceContext = new MyDbContext(sourceOptions);
using var targetContext = new MyDbContext(targetOptions);

var service = new DatabaseComparisonService(
    sourceContext: sourceContext,
    targetContext: targetContext,
    defaultMethod: ComparisonMethod.EntityFramework);

var report = await service.CompareAllTablesAsync();
```

#### Combined Comparison
```csharp
var service = new DatabaseComparisonService(
    sourceContext: sourceContext,
    targetContext: targetContext,
    sourceConnectionString: sourceConnectionString,
    targetConnectionString: targetConnectionString,
    defaultMethod: ComparisonMethod.Both);

var report = await service.CompareAllTablesAsync();
```

### 3. Specific Table Comparison

```csharp
var result = await service.CompareTableAsync("Users");

Console.WriteLine($"Table: {result.TableName}");
Console.WriteLine($"Status: {result.Status}");
Console.WriteLine($"Summary: {result.Summary}");

foreach (var columnDiff in result.ColumnDifferences)
{
    Console.WriteLine($"Column {columnDiff.ColumnName}: {columnDiff.Description}");
}
```

## Configuration

### appsettings.json Example

```json
{
  "ConnectionStrings": {
    "SourceDatabase": "Server=(localdb)\\mssqllocaldb;Database=SourceDb;Trusted_Connection=true;MultipleActiveResultSets=true",
    "TargetDatabase": "Server=(localdb)\\mssqllocaldb;Database=TargetDb;Trusted_Connection=true;MultipleActiveResultSets=true"
  },
  "DatabaseComparison": {
    "DefaultMethod": "RawSql",
    "SampleDataSize": 100,
    "EnableDataComparison": true,
    "EnableIndexComparison": true,
    "EnableForeignKeyComparison": true
  }
}
```

## Comparison Results

### ComparisonStatus Enum
- **Identical**: Tables are exactly the same
- **StructuralDifferences**: Differences in schema (columns, indexes, etc.)
- **DataDifferences**: Same structure, different data
- **BothDifferences**: Both structural and data differences
- **MissingInSource**: Table exists only in target database
- **MissingInTarget**: Table exists only in source database

### DifferenceType Enum
- **Missing**: Element exists in one database but not the other
- **Added**: Element added in target compared to source
- **Modified**: General modification
- **TypeChanged**: Data type changed
- **NullabilityChanged**: Nullable property changed
- **DefaultValueChanged**: Default value changed

## Advanced Features

### Service Capabilities
```csharp
var capabilities = service.GetCapabilities();

Console.WriteLine($"Entity Framework Support: {capabilities.SupportsEntityFramework}");
Console.WriteLine($"Raw SQL Support: {capabilities.SupportsRawSql}");
Console.WriteLine($"Data Comparison: {capabilities.SupportsDataComparison}");
Console.WriteLine($"Foreign Key Comparison: {capabilities.SupportsForeignKeyComparison}");
```

### Export to JSON
```csharp
var report = await service.CompareAllTablesAsync();

var options = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

var json = JsonSerializer.Serialize(report, options);
await File.WriteAllTextAsync("comparison_report.json", json);
```

### Get Available Tables
```csharp
var tableNames = await service.GetTableNamesAsync();
foreach (var tableName in tableNames)
{
    Console.WriteLine($"Available table: {tableName}");
}
```

## Sample Data Models

The service includes sample Entity Framework models for demonstration:

```csharp
public class User
{
    public int Id { get; set; }
    public string Email { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    
    public virtual ICollection<Order> Orders { get; set; }
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
}
```

## Demo Application

Run the included demo application to see all features in action:

```bash
dotnet run
```

The demo provides:
1. SQL-based comparison
2. Entity Framework-based comparison  
3. Combined EF + SQL comparison
4. Specific table comparison
5. Service capabilities display
6. Sample database generation
7. JSON report export

## Performance Considerations

### SQL vs Entity Framework
- **SQL approach**: More comprehensive, includes foreign keys, accurate statistics
- **EF approach**: Model-driven, limited to mapped entities, faster for simple comparisons
- **Combined approach**: Best accuracy but higher overhead

### Data Comparison
- Limited to configurable sample size (default: 100 rows)
- Requires primary key for data comparison
- Can be disabled for performance in large databases

### Optimization Tips
1. Use SQL approach for production comparisons
2. Disable data comparison for large tables
3. Compare specific tables instead of all tables when possible
4. Use connection pooling for better performance

## Error Handling

The service includes comprehensive error handling:

```csharp
var result = await service.CompareTableAsync("NonExistentTable");
if (!string.IsNullOrEmpty(result.Summary) && result.Summary.Contains("Error"))
{
    Console.WriteLine($"Comparison failed: {result.Summary}");
}
```

## Limitations

1. **SQL Server Only**: Currently designed for SQL Server databases
2. **Primary Key Required**: Data comparison requires primary keys
3. **Sample Data**: Full data comparison not recommended for large tables
4. **Schema Support**: Limited to basic schema elements (tables, columns, indexes, foreign keys)

## Extending the Service

### Custom Comparison Logic
```csharp
public class CustomTableComparisonService : SqlTableComparisonService
{
    public CustomTableComparisonService(string source, string target) 
        : base(source, target) { }
    
    // Override methods to add custom comparison logic
}
```

### Additional Database Support
Extend the service to support other database providers by:
1. Implementing provider-specific metadata queries
2. Adjusting SQL syntax for the target database
3. Creating provider-specific connection handling

## Contributing

1. Fork the repository
2. Create a feature branch
3. Add tests for new functionality
4. Submit a pull request

## License

This project is licensed under the MIT License.

## Support

For issues and questions:
1. Check the demo application for usage examples
2. Review the inline code documentation
3. Create an issue for bugs or feature requests