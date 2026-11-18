using System.Text;
using Microsoft.Data.SqlClient;

namespace MigrationSample;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Any(arg => arg is "--help" or "-h"))
        {
            Console.WriteLine("Usage: MigrationVerifier dbo.Customers dbo.CustomerContacts");
            Console.WriteLine("Comma-separated schema.table names are also accepted.");
            Console.WriteLine("Set MIG_SOURCE_CONNECTION and MIG_TARGET_CONNECTION.");
            Console.WriteLine("Writes migration-report.md in the current directory.");
            return args.Length == 0 ? 2 : 0;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var tables = args.SelectMany(arg => arg.Split(',', StringSplitOptions.TrimEntries))
                .Select(TableName.Parse).Distinct().ToArray();
            var sourceConnection = Environment.GetEnvironmentVariable("MIG_SOURCE_CONNECTION");
            var targetConnection = Environment.GetEnvironmentVariable("MIG_TARGET_CONNECTION");
            if (string.IsNullOrWhiteSpace(sourceConnection) || string.IsNullOrWhiteSpace(targetConnection))
                throw new ArgumentException("Both connection environment variables are required.");

            await using var source = new SqlConnection(sourceConnection);
            await using var target = new SqlConnection(targetConnection);
            await source.OpenAsync(cancellation.Token);
            await target.OpenAsync(cancellation.Token);
            var results = await Verifier.CompareAsync(source, target, tables, cancellation.Token);
            var matches = results.All(result => result.Matches);
            var report = Render(results, DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync("migration-report.md", report, new UTF8Encoding(false), cancellation.Token);
            Console.Write(report);
            return matches ? 0 : 1;
        }
        catch (Exception exception)
        {
            // Do not emit connection strings, credentials or SQL error details.
            var explanation = exception switch
            {
                OperationCanceledException => "Verification was cancelled.",
                ArgumentException => "Check the table arguments and connection environment variables.",
                NotSupportedException => "A column type or encryption configuration is unsupported. See README.md.",
                SqlException => "SQL operation failed. Check connectivity, permissions, snapshot isolation and the command timeout.",
                IOException => "The report could not be written to the current directory.",
                UnauthorizedAccessException => "The report directory is not writable.",
                _ => "Verification could not complete. Check table visibility, enabled primary keys and the documented prerequisites."
            };
            var report = $"# SQL Server migration verification\n\nGenerated (UTC): {DateTimeOffset.UtcNow:O}\n\nResult: ERROR — verification incomplete.\n\n{explanation}\n\nError category: {exception.GetType().Name}. No successful comparison is claimed.\n";
            Console.Error.Write(report);
            try
            {
                await File.WriteAllTextAsync("migration-report.md", report, new UTF8Encoding(false));
            }
            catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine("Unable to save migration-report.md; any existing file may be stale.");
            }
            return 2;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("|", "&#124;").Replace("`", "&#96;")
        .Replace("\\", "&#92;").Replace("\r", "\\r").Replace("\n", "\\n");

    private static string Render(IReadOnlyList<TableComparison> results, DateTimeOffset generatedAt)
    {
        var output = new StringBuilder();
        output.AppendLine("# SQL Server migration verification").AppendLine();
        output.AppendLine($"Generated (UTC): {generatedAt:O}").AppendLine();
        output.AppendLine(results.All(result => result.Matches)
            ? "Result: MATCH — all selected tables match."
            : "Result: DIFFERENCES — one or more selected tables differ.").AppendLine();
        output.AppendLine("Missing means present only in source; extra means present only in target. Changed means the same serialized primary key has a different SHA-256 row hash.").AppendLine();
        output.AppendLine("| Table | Source rows | Target rows | Missing | Extra | Changed | Result |");
        output.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var result in results)
        {
            var missing = result.SchemaDifference is null ? result.MissingCount.ToString() : "n/a";
            var extra = result.SchemaDifference is null ? result.ExtraCount.ToString() : "n/a";
            var changed = result.SchemaDifference is null ? result.ChangedCount.ToString() : "n/a";
            var status = result.SchemaDifference is not null ? "SCHEMA DIFFERENCE"
                : result.Matches ? "MATCH" : "DIFFERENCES";
            output.AppendLine($"| {Escape(result.Table)} | {result.SourceRows} | {result.TargetRows} | {missing} | {extra} | {changed} | {status} |");
        }

        foreach (var result in results.Where(result => !result.Matches))
        {
            output.AppendLine().AppendLine($"## {Escape(result.Table)}").AppendLine();
            if (result.SchemaDifference is not null)
            {
                output.AppendLine(result.SchemaDifference);
                continue;
            }
            AppendKeys(output, "Missing keys", result.MissingKeys, result.MissingCount);
            AppendKeys(output, "Extra keys", result.ExtraKeys, result.ExtraCount);
            AppendKeys(output, "Changed keys", result.ChangedKeys, result.ChangedCount);
        }
        output.AppendLine().AppendLine("## Scope").AppendLine();
        output.AppendLine("Selected tables only; independent database snapshots. Pause writes before acceptance. Counts cover all scanned rows; key examples are capped at 20 per category and table, ordered by serialized key bytes. Hash equality is probabilistic and does not validate constraints, indexes, procedures or application behavior. See README.md for supported types and limits.");
        return output.ToString();
    }

    private static void AppendKeys(StringBuilder output, string title, IReadOnlyList<string> keys, long total)
    {
        if (total == 0) return;
        output.AppendLine($"### {title} — showing {keys.Count} of {total}").AppendLine();
        foreach (var key in keys) output.AppendLine($"- {Escape(key)}");
        output.AppendLine();
    }
}
