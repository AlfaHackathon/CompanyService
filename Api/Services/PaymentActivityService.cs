using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using Api.Common;
using Api.Contracts.Payments;
using Api.Repositories;

namespace Api.Services;

public sealed class PaymentActivityService(IPaymentRows rows)
{
    public Task<Result<PaymentRowReceipt>> Save(PaymentRowInput input) => Run(async () =>
    {
        if (!PaymentRowValidation.Valid(input)) throw PaymentActivityError.Invalid();
        return await rows.Save(input);
    });
    public Task<Result<PaymentActivity>> Activity(Guid companyId, string sourceId, string sourceVersionId, DateTimeOffset asOf) => Run(async () =>
    {
        if (companyId == Guid.Empty || !PaymentRowValidation.Identity(sourceId) || !PaymentRowValidation.Identity(sourceVersionId) || asOf == default)
            throw PaymentActivityError.Invalid();
        return await rows.Activity(companyId, sourceId, sourceVersionId, asOf) ?? throw PaymentActivityError.Missing();
    });
    private static async Task<Result<T>> Run<T>(Func<Task<T>> action)
    {
        try { return new(await action(), null); }
        catch (PaymentActivityError error) { return new(new Error(error.Message, ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = error.Status }); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new(new Error(PaymentActivityError.Unavailable().Message, ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = PaymentActivityError.Unavailable().Status });
        }
    }
}
