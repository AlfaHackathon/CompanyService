using System.Net;

namespace Api.Services;

public sealed class PaymentActivityError(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public static PaymentActivityError Invalid() => new(HttpStatusCode.BadRequest, "Payment source identities, UUID parties and exact numeric strings are required within the request resource bounds.");
    public static PaymentActivityError Missing() => new(HttpStatusCode.NotFound, "No observed payment activity is known for the selected company, source version and asOf.");
    public static PaymentActivityError Unavailable() => new(HttpStatusCode.ServiceUnavailable, "Payment data was not confirmed. Preserve unknowns; after an uncertain write outcome retry the identical source row and metadata.");
}
