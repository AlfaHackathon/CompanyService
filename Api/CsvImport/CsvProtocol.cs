using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Api.CsvImport;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CsvBatch(string SourceId, string VersionId, string ManifestHash, string ManifestJson,
    string Phase, long StartRow, CsvRow[] Rows);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CsvRow(long Number, string[] Values);
public sealed record CsvReceipt(string SourceId, string VersionId, string ManifestHash, string Phase,
    long StartRow, long EndRow, int Rows, string RequestHash, int Skipped = 0, int Conflicts = 0);

public static class CsvProtocol
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool Id(string? value) => value is { Length: > 0 and <= 200 } && value.Trim() == value && !value.Any(char.IsControl);
    public static bool Sha(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static Guid Uuid(string value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty && id.ToString("D") == value
        ? id : throw new InvalidDataException("invalid_uuid");

    public static JsonDocument Validate(CsvBatch batch, string[] phases)
    {
        if (!Id(batch.SourceId) || !Id(batch.VersionId) || !Sha(batch.ManifestHash) || batch.ManifestJson is null ||
            Encoding.UTF8.GetByteCount(batch.ManifestJson) > 100000 || Hash(batch.ManifestJson) != batch.ManifestHash ||
            !phases.Contains(batch.Phase) || batch.StartRow < 1 || batch.Rows is not { Length: > 0 and <= 500 })
            throw new InvalidDataException("invalid_batch");
        var manifest = JsonDocument.Parse(batch.ManifestJson);
        try
        {
            var root = manifest.RootElement;
            if (root.GetProperty("source_id").GetString() != batch.SourceId || root.GetProperty("version").GetString() != batch.VersionId ||
                root.GetProperty("format").GetInt32() != 1) throw new InvalidDataException("manifest_identity");
            var file = root.GetProperty("files").GetProperty(batch.Phase);
            var headers = file.GetProperty("headers").EnumerateArray().Select(x => x.GetString()!).ToArray();
            if (headers.Length == 0 || headers.Any(string.IsNullOrEmpty) || headers.Distinct(StringComparer.Ordinal).Count() != headers.Length ||
                !Sha(file.GetProperty("sha256").GetString()) || checked(batch.StartRow + batch.Rows.Length - 1) > file.GetProperty("rows").GetInt64())
                throw new InvalidDataException("manifest_file");
            for (var i = 0; i < batch.Rows.Length; i++)
                if (batch.Rows[i] is not { } row || row.Number != batch.StartRow + i || row.Values is null || row.Values.Length != headers.Length ||
                    row.Values.Any(x => x is null || x.Contains('\0')))
                    throw new InvalidDataException("invalid_row");
            return manifest;
        }
        catch { manifest.Dispose(); throw; }
    }

    public static Dictionary<string, string> Fields(JsonElement manifest, CsvBatch batch, CsvRow row)
    {
        var headers = manifest.GetProperty("files").GetProperty(batch.Phase).GetProperty("headers");
        return headers.EnumerateArray().Select((x, i) => (Key: x.GetString()!, Value: row.Values[i])).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
    }
    public static string Cell(JsonElement manifest, CsvBatch batch, Dictionary<string, string> fields, string key) =>
        fields[manifest.GetProperty("files").GetProperty(batch.Phase).GetProperty("columns").GetProperty(key).GetString()!];
    public static CsvReceipt Receipt(CsvBatch b, string hash) => new(b.SourceId, b.VersionId, b.ManifestHash, b.Phase,
        b.StartRow, b.StartRow + b.Rows.Length - 1, b.Rows.Length, hash);
}
