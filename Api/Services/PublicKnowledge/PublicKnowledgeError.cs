using System.Net;

namespace Api.Services.PublicKnowledge;

public sealed class PublicKnowledgeError(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public static PublicKnowledgeError Invalid() => new(HttpStatusCode.BadRequest, "Invalid bounded company knowledge request.");
    public static PublicKnowledgeError Missing() => new(HttpStatusCode.NotFound, "Company knowledge or material was not found in the current scope.");
    public static PublicKnowledgeError Unavailable() => new(HttpStatusCode.ServiceUnavailable, "Company knowledge is unavailable. A write outcome may be unknown; retry the identical request with the same Idempotency-Key.");
}
