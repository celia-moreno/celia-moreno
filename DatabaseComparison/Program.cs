using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using DatabaseComparison.Services;
using DatabaseComparison.Sample;
using DatabaseComparison.Models;
using System.Text.Json;

namespace DatabaseComparison
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Database Table Comparison Service Demo ===\n");

            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            var sourceConnectionString = configuration.GetConnectionString("SourceDatabase")!;
            var targetConnectionString = configuration.GetConnectionString("TargetDatabase")!;

            Console.WriteLine("Available Demo Options:");
            Console.WriteLine("1. SQL-based comparison (recommended)");
            Console.WriteLine("2. Entity Framework-based comparison");
            Console.WriteLine("3. Combined EF + SQL comparison");
            Console.WriteLine("4. Compare specific table");
            Console.WriteLine("5. Show service capabilities");
            Console.WriteLine("6. Generate sample databases");
            Console.WriteLine("7. Export comparison report to JSON");
            Console.WriteLine();

            while (true)
            {
                Console.Write("Enter your choice (1-7, or 'q' to quit): ");
                var choice = Console.ReadLine()?.Trim().ToLower();

                if (choice == "q" || choice == "quit")
                    break;

                try
                {
                    switch (choice)
                    {
                        case "1":
                            await DemoSqlComparison(sourceConnectionString, targetConnectionString);
                            break;
                        case "2":
                            await DemoEfComparison(sourceConnectionString, targetConnectionString);
                            break;
                        case "3":
                            await DemoCombinedComparison(sourceConnectionString, targetConnectionString);
                            break;
                        case "4":
                            await DemoSpecificTableComparison(sourceConnectionString, targetConnectionString);
                            break;
                        case "5":
                            await DemoServiceCapabilities(sourceConnectionString, targetConnectionString);
                            break;
                        case "6":
                            await GenerateSampleDatabases(sourceConnectionString, targetConnectionString);
                            break;
                        case "7":
                            await ExportComparisonReport(sourceConnectionString, targetConnectionString);
                            break;
                        default:
                            Console.WriteLine("Invalid choice. Please try again.\n");
                            continue;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}\n");
                }

                Console.WriteLine("\nPress Enter to continue...");
                Console.ReadLine();
                Console.Clear();
                Console.WriteLine("=== Database Table Comparison Service Demo ===\n");
            }

            Console.WriteLine("Thanks for using the Database Comparison Service!");
        }

        static async Task DemoSqlComparison(string sourceConnectionString, string targetConnectionString)
        {
            Console.WriteLine("\n=== SQL-based Table Comparison ===");
            
            var service = new DatabaseComparisonService(
                sourceConnectionString: sourceConnectionString,
                targetConnectionString: targetConnectionString,
                defaultMethod: ComparisonMethod.RawSql);

            var report = await service.CompareAllTablesAsync();
            
            DisplayComparisonReport(report);
        }

        static async Task DemoEfComparison(string sourceConnectionString, string targetConnectionString)
        {
            Console.WriteLine("\n=== Entity Framework-based Table Comparison ===");

            // Create EF contexts
            var sourceOptions = new DbContextOptionsBuilder<SampleDbContext>()
                .UseSqlServer(sourceConnectionString)
                .Options;
            var targetOptions = new DbContextOptionsBuilder<SampleDbContext>()
                .UseSqlServer(targetConnectionString)
                .Options;

            using var sourceContext = new SampleDbContext(sourceOptions);
            using var targetContext = new SampleDbContext(targetOptions);

            var service = new DatabaseComparisonService(
                sourceContext: sourceContext,
                targetContext: targetContext,
                defaultMethod: ComparisonMethod.EntityFramework);

            var report = await service.CompareAllTablesAsync();
            
            DisplayComparisonReport(report);
        }

        static async Task DemoCombinedComparison(string sourceConnectionString, string targetConnectionString)
        {
            Console.WriteLine("\n=== Combined EF + SQL Table Comparison ===");

            // Create EF contexts
            var sourceOptions = new DbContextOptionsBuilder<SampleDbContext>()
                .UseSqlServer(sourceConnectionString)
                .Options;
            var targetOptions = new DbContextOptionsBuilder<SampleDbContext>()
                .UseSqlServer(targetConnectionString)
                .Options;

            using var sourceContext = new SampleDbContext(sourceOptions);
            using var targetContext = new SampleDbContext(targetOptions);

            var service = new DatabaseComparisonService(
                sourceContext: sourceContext,
                targetContext: targetContext,
                sourceConnectionString: sourceConnectionString,
                targetConnectionString: targetConnectionString,
                defaultMethod: ComparisonMethod.Both);

            var report = await service.CompareAllTablesAsync();
            
            DisplayComparisonReport(report);
        }

        static async Task DemoSpecificTableComparison(string sourceConnectionString, string targetConnectionString)
        {
            Console.WriteLine("\n=== Specific Table Comparison ===");

            var service = new DatabaseComparisonService(
                sourceConnectionString: sourceConnectionString,
                targetConnectionString: targetConnectionString);

            // Get available tables
            var tableNames = await service.GetTableNamesAsync();
            
            if (!tableNames.Any())
            {
                Console.WriteLine("No tables found in the databases.");
                return;
            }

            Console.WriteLine("Available tables:");
            for (int i = 0; i < tableNames.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {tableNames[i]}");
            }

            Console.Write("\nEnter table number to compare: ");
            if (int.TryParse(Console.ReadLine(), out int tableIndex) && 
                tableIndex > 0 && tableIndex <= tableNames.Count)
            {
                var tableName = tableNames[tableIndex - 1];
                var result = await service.CompareTableAsync(tableName);
                
                DisplayTableComparisonResult(result);
            }
            else
            {
                Console.WriteLine("Invalid table selection.");
            }
        }

        static async Task DemoServiceCapabilities(string sourceConnectionString, string targetConnectionString)
        {
            Console.WriteLine("\n=== Service Capabilities ===");

            var service = new DatabaseComparisonService(
                sourceConnectionString: sourceConnectionString,
                targetConnectionString: targetConnectionString);

            var capabilities = service.GetCapabilities();

            Console.WriteLine($"Entity Framework Support: {(capabilities.SupportsEntityFramework ? "✓" : "✗")}");
            Console.WriteLine($"Raw SQL Support: {(capabilities.SupportsRawSql ? "✓" : "✗")}");
            Console.WriteLine($"Combined Methods: {(capabilities.SupportsBothMethods ? "✓" : "✗")}");
            Console.WriteLine($"Structural Comparison: {(capabilities.SupportsStructuralComparison ? "✓" : "✗")}");
            Console.WriteLine($"Data Comparison: {(capabilities.SupportsDataComparison ? "✓" : "✗")}");
            Console.WriteLine($"Index Comparison: {(capabilities.SupportsIndexComparison ? "✓" : "✗")}");
            Console.WriteLine($"Foreign Key Comparison: {(capabilities.SupportsForeignKeyComparison ? "✓" : "✗")}");
        }

        static async Task GenerateSampleDatabases(string sourceConnectionString, string targetConnectionString)
        {
            Console.WriteLine("\n=== Generating Sample Databases ===");

            try
            {
                // Create source database
                var sourceOptions = new DbContextOptionsBuilder<SampleDbContext>()
                    .UseSqlServer(sourceConnectionString)
                    .Options;

                using (var sourceContext = new SampleDbContext(sourceOptions))
                {
                    await sourceContext.Database.EnsureDeletedAsync();
                    await sourceContext.Database.EnsureCreatedAsync();
                    
                    // Add sample data
                    await SeedSampleData(sourceContext);
                    Console.WriteLine("Source database created and seeded.");
                }

                // Create target database with slight differences
                var targetOptions = new DbContextOptionsBuilder<SampleDbContext>()
                    .UseSqlServer(targetConnectionString)
                    .Options;

                using (var targetContext = new SampleDbContext(targetOptions))
                {
                    await targetContext.Database.EnsureDeletedAsync();
                    await targetContext.Database.EnsureCreatedAsync();
                    
                    // Add sample data with differences
                    await SeedSampleDataWithDifferences(targetContext);
                    Console.WriteLine("Target database created and seeded with differences.");
                }

                Console.WriteLine("\nSample databases are ready for comparison!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating sample databases: {ex.Message}");
            }
        }

        static async Task ExportComparisonReport(string sourceConnectionString, string targetConnectionString)
        {
            Console.WriteLine("\n=== Exporting Comparison Report ===");

            var service = new DatabaseComparisonService(
                sourceConnectionString: sourceConnectionString,
                targetConnectionString: targetConnectionString);

            var report = await service.CompareAllTablesAsync();

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(report, options);
            var fileName = $"database_comparison_report_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            
            await File.WriteAllTextAsync(fileName, json);
            
            Console.WriteLine($"Report exported to: {fileName}");
            Console.WriteLine($"File size: {new FileInfo(fileName).Length} bytes");
        }

        static void DisplayComparisonReport(DatabaseComparisonReport report)
        {
            Console.WriteLine($"\nComparison completed at: {report.ComparisonDate}");
            Console.WriteLine($"Method used: {report.ComparisonMethod}");
            Console.WriteLine($"Summary: {report.Summary}");

            if (!string.IsNullOrEmpty(report.ErrorMessage))
            {
                Console.WriteLine($"Error: {report.ErrorMessage}");
                return;
            }

            Console.WriteLine("\n=== Statistics ===");
            Console.WriteLine($"Total tables: {report.TotalTables}");
            Console.WriteLine($"Identical tables: {report.IdenticalTables}");
            Console.WriteLine($"Tables with differences: {report.TablesWithDifferences}");
            Console.WriteLine($"Total column differences: {report.TotalColumnDifferences}");
            Console.WriteLine($"Total index differences: {report.TotalIndexDifferences}");
            Console.WriteLine($"Total data differences: {report.TotalDataDifferences}");

            Console.WriteLine("\n=== Table Details ===");
            foreach (var table in report.TableResults.OrderBy(t => t.TableName))
            {
                DisplayTableComparisonResult(table, showDetails: false);
            }

            // Show detailed differences for tables with issues
            var tablesWithDifferences = report.TableResults.Where(t => t.Status != ComparisonStatus.Identical).ToList();
            if (tablesWithDifferences.Any())
            {
                Console.WriteLine("\n=== Detailed Differences ===");
                foreach (var table in tablesWithDifferences)
                {
                    DisplayTableComparisonResult(table, showDetails: true);
                }
            }
        }

        static void DisplayTableComparisonResult(TableComparisonResult result, bool showDetails = true)
        {
            var statusIcon = result.Status switch
            {
                ComparisonStatus.Identical => "✓",
                ComparisonStatus.StructuralDifferences => "⚠",
                ComparisonStatus.DataDifferences => "△",
                ComparisonStatus.BothDifferences => "✗",
                ComparisonStatus.MissingInSource => "←",
                ComparisonStatus.MissingInTarget => "→",
                _ => "?"
            };

            Console.WriteLine($"{statusIcon} {result.TableName} - {result.Status}");
            Console.WriteLine($"   {result.Summary}");

            if (showDetails && (result.ColumnDifferences.Any() || result.IndexDifferences.Any() || result.DataDifferences.Any()))
            {
                if (result.ColumnDifferences.Any())
                {
                    Console.WriteLine("   Column Differences:");
                    foreach (var diff in result.ColumnDifferences.Take(5)) // Show first 5
                    {
                        Console.WriteLine($"     • {diff.ColumnName}: {diff.Description}");
                    }
                    if (result.ColumnDifferences.Count > 5)
                        Console.WriteLine($"     ... and {result.ColumnDifferences.Count - 5} more");
                }

                if (result.IndexDifferences.Any())
                {
                    Console.WriteLine("   Index Differences:");
                    foreach (var diff in result.IndexDifferences.Take(3)) // Show first 3
                    {
                        Console.WriteLine($"     • {diff.IndexName}: {diff.Description}");
                    }
                    if (result.IndexDifferences.Count > 3)
                        Console.WriteLine($"     ... and {result.IndexDifferences.Count - 3} more");
                }

                if (result.DataDifferences.Any())
                {
                    Console.WriteLine("   Data Differences:");
                    foreach (var diff in result.DataDifferences.Take(3)) // Show first 3
                    {
                        Console.WriteLine($"     • Row {diff.PrimaryKeyValue}, Column {diff.ColumnName}: {diff.Description}");
                    }
                    if (result.DataDifferences.Count > 3)
                        Console.WriteLine($"     ... and {result.DataDifferences.Count - 3} more");
                }
            }

            Console.WriteLine();
        }

        static async Task SeedSampleData(SampleDbContext context)
        {
            // Add users
            var users = new[]
            {
                new User { Email = "john@example.com", FirstName = "John", LastName = "Doe" },
                new User { Email = "jane@example.com", FirstName = "Jane", LastName = "Smith" },
                new User { Email = "bob@example.com", FirstName = "Bob", LastName = "Johnson" }
            };
            context.Users.AddRange(users);

            // Add products
            var products = new[]
            {
                new Product { Name = "Laptop", Price = 999.99m, StockQuantity = 10 },
                new Product { Name = "Mouse", Price = 29.99m, StockQuantity = 50 },
                new Product { Name = "Keyboard", Price = 89.99m, StockQuantity = 25 }
            };
            context.Products.AddRange(products);

            await context.SaveChangesAsync();
        }

        static async Task SeedSampleDataWithDifferences(SampleDbContext context)
        {
            // Add users with some differences
            var users = new[]
            {
                new User { Email = "john@example.com", FirstName = "John", LastName = "Doe" },
                new User { Email = "jane@example.com", FirstName = "Jane", LastName = "Smith" },
                new User { Email = "alice@example.com", FirstName = "Alice", LastName = "Brown" } // Different user
            };
            context.Users.AddRange(users);

            // Add products with different prices
            var products = new[]
            {
                new Product { Name = "Laptop", Price = 1099.99m, StockQuantity = 8 }, // Different price and stock
                new Product { Name = "Mouse", Price = 29.99m, StockQuantity = 50 },
                new Product { Name = "Keyboard", Price = 89.99m, StockQuantity = 25 },
                new Product { Name = "Monitor", Price = 299.99m, StockQuantity = 15 } // Additional product
            };
            context.Products.AddRange(products);

            await context.SaveChangesAsync();
        }
    }
}