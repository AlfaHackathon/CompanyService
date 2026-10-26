using System.Text.Json.Serialization;

namespace Api.Contracts.Payments;

// Source metadata identifies an immutable logical artifact or caller-supplied manifest.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PaymentRowInput(string ImportId, string SourceId, string SourceVersionId,
    string SourceContentHash, string SourceRowId, Guid BeneficiaryCompanyId, Guid PayerCompanyId, string PayAmt, string PayCnt);
public sealed record PaymentRowReceipt(PaymentRowInput SourceRaw, DateTimeOffset KnownAt, string Watermark, bool SelfTransfer);
public sealed record PaymentActivityBucket(string PayAmt, string PayCnt, string ObservedRowCount);
public sealed record PaymentActivityPeriod(string Basis, int? Year, string? End);
public sealed record PaymentActivityCoverage(bool? Complete, string ObservedRowCount, string Watermark);
public sealed record PaymentActivityProvenance(DateTimeOffset SourceKnownAt, DateTimeOffset ObservedKnownAt, string Basis);
public sealed record PaymentActivity(Guid CompanyId, string SourceId, string SourceVersionId, string SourceContentHash,
    DateTimeOffset AsOf, string Currency, PaymentActivityPeriod Period, PaymentActivityBucket Incoming,
    PaymentActivityBucket Outgoing, PaymentActivityBucket SelfTransfer, PaymentActivityCoverage Coverage,
    PaymentActivityProvenance Provenance);
