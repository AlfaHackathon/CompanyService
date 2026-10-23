using System.Text.Json;

namespace Api.Common;

public static class KnowledgeJson
{
    // These options define canonical immutable payloads and content hashes.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
