using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace MigrationSample;

public sealed record TableName(string Schema, string Name)
{
    public string Sql => Quote(Schema) + "." + Quote(Name);
    public override string ToString() => Schema + "." + Name;

    public static TableName Parse(string value)
    {
        // Restrict CLI syntax; quote identifiers independently in every query.
        if (!Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*$"))
            throw new ArgumentException("Use schema.table identifiers, for example dbo.Customers.");
        var parts = value.Split('.');
        if (parts.Any(part => part.Length > 128))
            throw new ArgumentException("SQL Server identifiers cannot exceed 128 characters.");
        return new(parts[0], parts[1]);
    }

    public static string Quote(string value) => "[" + value.Replace("]", "]]") + "]";
}

public sealed record TableComparison(
    string Table, long SourceRows, long TargetRows, string? SchemaDifference,
    long MissingCount, long ExtraCount, long ChangedCount,
    IReadOnlyList<string> MissingKeys, IReadOnlyList<string> ExtraKeys,
    IReadOnlyList<string> ChangedKeys)
{
    public bool Matches => SchemaDifference is null && SourceRows == TargetRows
        && MissingCount == 0 && ExtraCount == 0 && ChangedCount == 0;
}

public static class Verifier
{
    private sealed record Column(
        string Name, string Type, short MaxLength, byte Precision, byte Scale,
        bool Nullable, string? Collation, int PrimaryKeyOrdinal,
        bool Computed, bool UserDefined, bool Encrypted);

    private sealed record Row(string DisplayKey, byte[] Hash);

    public static async Task<IReadOnlyList<TableComparison>> CompareAsync(
        SqlConnection source, SqlConnection target, IReadOnlyList<TableName> tables,
        CancellationToken cancellationToken = default)
    {
        // Each database must have ALLOW_SNAPSHOT_ISOLATION enabled. These are
        // independent snapshots; pause writes during migration acceptance.
        await using var sourceTransaction = (SqlTransaction)await source.BeginTransactionAsync(
            IsolationLevel.Snapshot, cancellationToken);
        await using var targetTransaction = (SqlTransaction)await target.BeginTransactionAsync(
            IsolationLevel.Snapshot, cancellationToken);

        var results = new List<TableComparison>();
        foreach (var table in tables)
        {
            var sourceColumns = await ColumnsAsync(source, sourceTransaction, table, cancellationToken);
            var targetColumns = await ColumnsAsync(target, targetTransaction, table, cancellationToken);
            var sourceCount = await CountAsync(source, sourceTransaction, table, cancellationToken);
            var targetCount = await CountAsync(target, targetTransaction, table, cancellationToken);

            if (!sourceColumns.SequenceEqual(targetColumns))
            {
                results.Add(new(table.ToString(), sourceCount, targetCount,
                    "Column definitions or primary-key definitions differ; row hashes were not compared.",
                    0, 0, 0, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
                continue;
            }

            ValidateColumns(table, sourceColumns);
            var sourceRows = await ReadRowsAsync(source, sourceTransaction, table, sourceColumns, cancellationToken);
            var targetRows = await ReadRowsAsync(target, targetTransaction, table, targetColumns, cancellationToken);
            if ((long)sourceRows.Count != sourceCount || (long)targetRows.Count != targetCount)
                throw new InvalidOperationException($"{table}: row count and scanned key count disagree.");

            long missing = 0, extra = 0, changed = 0;
            var missingKeys = new List<string>();
            var extraKeys = new List<string>();
            var changedKeys = new List<string>();

            // Full framed key bytes identify rows. Keys are not reduced to hashes.
            foreach (var entry in sourceRows.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!targetRows.TryGetValue(entry.Key, out var targetRow))
                {
                    missing++;
                    if (missingKeys.Count < 20) missingKeys.Add(entry.Value.DisplayKey);
                }
                else if (!entry.Value.Hash.AsSpan().SequenceEqual(targetRow.Hash))
                {
                    changed++;
                    if (changedKeys.Count < 20) changedKeys.Add(entry.Value.DisplayKey);
                }
            }

            foreach (var entry in targetRows.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!sourceRows.ContainsKey(entry.Key))
                {
                    extra++;
                    if (extraKeys.Count < 20) extraKeys.Add(entry.Value.DisplayKey);
                }
            }

            results.Add(new(table.ToString(), sourceCount, targetCount, null,
                missing, extra, changed, missingKeys, extraKeys, changedKeys));
        }

        await sourceTransaction.CommitAsync(cancellationToken);
        await targetTransaction.CommitAsync(cancellationToken);
        return results;
    }

    private static SqlCommand Command(SqlConnection connection, SqlTransaction transaction, string sql)
        => new(sql, connection, transaction) { CommandTimeout = 180 };

    private static async Task<List<Column>> ColumnsAsync(
        SqlConnection connection, SqlTransaction transaction, TableName table,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.[name], t.[name], c.[max_length], c.[precision], c.[scale],
                   c.[is_nullable], c.[collation_name],
                   CONVERT(int, COALESCE(pk.[key_ordinal], 0)),
                   c.[is_computed], t.[is_user_defined],
                   CONVERT(bit, CASE WHEN c.[encryption_type] IS NULL THEN 0 ELSE 1 END)
            FROM sys.tables AS tb
            JOIN sys.schemas AS s ON s.[schema_id] = tb.[schema_id]
            JOIN sys.columns AS c ON c.[object_id] = tb.[object_id]
            JOIN sys.types AS t ON t.[user_type_id] = c.[user_type_id]
            LEFT JOIN (
                SELECT ic.[object_id], ic.[column_id], ic.[key_ordinal]
                FROM sys.indexes AS i
                JOIN sys.index_columns AS ic
                  ON ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
                WHERE i.[is_primary_key] = 1 AND i.[is_disabled] = 0
                  AND i.[is_hypothetical] = 0 AND ic.[key_ordinal] > 0
            ) AS pk ON pk.[object_id] = c.[object_id] AND pk.[column_id] = c.[column_id]
            WHERE s.[name] = @schema AND tb.[name] = @table
            ORDER BY c.[column_id];
            """;
        using var command = Command(connection, transaction, sql);
        command.Parameters.Add("@schema", SqlDbType.NVarChar, 128).Value = table.Schema;
        command.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = table.Name;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<Column>();
        while (await reader.ReadAsync(cancellationToken))
            columns.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt16(2),
                reader.GetByte(3), reader.GetByte(4), reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetInt32(7),
                reader.GetBoolean(8), reader.GetBoolean(9), reader.GetBoolean(10)));
        if (columns.Count == 0)
            throw new InvalidOperationException($"{table}: table is absent or metadata is not visible.");
        return columns;
    }

    private static async Task<long> CountAsync(
        SqlConnection connection, SqlTransaction transaction, TableName table,
        CancellationToken cancellationToken)
    {
        using var command = Command(connection, transaction, $"SELECT COUNT_BIG(*) FROM {table.Sql};");
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static void ValidateColumns(TableName table, IReadOnlyList<Column> columns)
    {
        if (!columns.Any(column => column.PrimaryKeyOrdinal > 0))
            throw new InvalidOperationException($"{table}: an enabled primary key is required.");
        foreach (var column in columns)
        {
            if (column.UserDefined || column.Encrypted)
                throw new NotSupportedException($"{table}.{column.Name}: user-defined or encrypted columns are unsupported.");
            // Build expressions now so unsupported types fail even for empty tables.
            _ = Payload(column);
        }
    }

    private static string TextValue(Column column)
    {
        var name = TableName.Quote(column.Name);
        return column.Type switch
        {
            "char" or "varchar" or "nchar" or "nvarchar"
                or "bit" or "tinyint" or "smallint" or "int" or "bigint"
                or "decimal" or "numeric" or "uniqueidentifier"
                => $"CONVERT(nvarchar(max), {name})",
            "float" or "real" => $"CONVERT(nvarchar(max), {name}, 3)",
            "money" or "smallmoney" => $"CONVERT(nvarchar(max), {name}, 2)",
            "date" or "datetime" or "datetime2" or "smalldatetime" or "time"
                => $"CONVERT(nvarchar(max), {name}, 126)",
            "datetimeoffset" => $"CONVERT(nvarchar(max), {name}, 127)",
            "binary" or "varbinary" or "timestamp" or "rowversion"
                => $"CONVERT(nvarchar(max), CONVERT(varchar(max), {name}, 2))",
            _ => throw new NotSupportedException($"Column {column.Name}: SQL type {column.Type} is unsupported; no silent exclusions are made.")
        };
    }

    private static string Payload(Column column)
    {
        if (column.Type is "binary" or "varbinary" or "timestamp" or "rowversion")
            return $"CONVERT(varbinary(max), {TableName.Quote(column.Name)})";
        return $"CONVERT(varbinary(max), {TextValue(column)})";
    }

    private static string Frame(Column column)
    {
        var name = TableName.Quote(column.Name);
        var payload = Payload(column);
        // NULL = 00. Non-NULL = 01 + eight-byte byte length + payload.
        // MAX operands prevent the 8,000-byte binary concatenation truncation.
        return $"(CASE WHEN {name} IS NULL THEN CONVERT(varbinary(max), 0x00) " +
            $"ELSE CONVERT(varbinary(max), 0x01) + CONVERT(binary(8), " +
            $"CONVERT(bigint, DATALENGTH({payload}))) + {payload} END)";
    }

    private static string SerializeColumns(IEnumerable<Column> columns)
        => "CONVERT(varbinary(max), 0x) + " + string.Join(" + ", columns.Select(Frame));

    private static async Task<Dictionary<string, Row>> ReadRowsAsync(
        SqlConnection connection, SqlTransaction transaction, TableName table,
        IReadOnlyList<Column> columns, CancellationToken cancellationToken)
    {
        var keys = columns.Where(column => column.PrimaryKeyOrdinal > 0)
            .OrderBy(column => column.PrimaryKeyOrdinal).ToArray();
        var displayColumns = string.Join(", ", keys.Select(TextValue));
        var sql = $"SELECT {displayColumns}, {SerializeColumns(keys)}, " +
            $"HASHBYTES('SHA2_256', {SerializeColumns(columns)}) FROM {table.Sql};";
        using var command = Command(connection, transaction, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new Dictionary<string, Row>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var display = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < keys.Length; index++)
                display.Add(keys[index].Name, reader.GetString(index));
            var key = Convert.ToHexString(reader.GetFieldValue<byte[]>(keys.Length));
            var hash = reader.GetFieldValue<byte[]>(keys.Length + 1);
            if (hash.Length != 32)
                throw new InvalidOperationException($"{table}: SHA-256 did not return 32 bytes.");
            if (!rows.TryAdd(key, new(JsonSerializer.Serialize(display), hash)))
                throw new InvalidOperationException($"{table}: duplicate serialized primary key.");
        }
        return rows;
    }
}
