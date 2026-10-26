using Api.Controllers;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Api.Services.PublicKnowledge;

// Uses the existing AddSwaggerAuth integration; does not alter runtime authorization.
public sealed class CompanyKnowledgeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(CompanyKnowledgeController)) return;
        var write = context.MethodInfo.Name is "Put" or "Create";
        operation.Tags = [new OpenApiTag { Name = "Current company knowledge" }];
        operation.Description = "Current membership is resolved server-side. Reads require source company/bank permission; writes are current-creator only. No client company/hash/time/provenance is accepted. Responses are private, no-store. Materials are user declarations, not verified bank evidence. List coverage is canonical_changes_only, not exhaustive business evidence.";
        if (write)
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "Idempotency-Key",
                In = ParameterLocation.Header,
                Required = true,
                Description = "One stable key per normalized operation. After a timeout/503 the write may have committed; retry identical input with the same key. A different input under the same key returns 409.",
                Schema = new OpenApiSchema { Type = "string", MinLength = 1, MaxLength = 128, Pattern = "^[!-~]{1,128}$" }
            });
        }
        foreach (var pair in new Dictionary<string, string>
        {
            ["400"] = "Invalid bounded input, query/cursor or required Idempotency-Key.",
            ["401"] = "Authentication or trusted identity missing.",
            ["403"] = "Only the current company creator may write.",
            ["404"] = "Current membership, knowledge or source unavailable in current permissions.",
            ["409"] = "Idempotency-Key input conflict, immutable version conflict or membership context changed.",
            ["413"] = "HTTP body exceeds 1 MiB.",
            ["503"] = "Dependency/storage unavailable. No empty success is implied; a write outcome may be unknown. Retry identical input with the same Idempotency-Key."
        }) operation.Responses.TryAdd(pair.Key, new OpenApiResponse { Description = pair.Value });
    }
}
