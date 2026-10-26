using System.Text.RegularExpressions;
using Api.Contracts.Payments;

namespace Api.Common;

public static class PaymentRowValidation
{
    public const int MaximumDigits = 4096;
    public static bool Identity(string? value, int maximum = 200) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum && !value.Any(char.IsControl);
    public static bool Valid(PaymentRowInput? row) => row is not null && Identity(row.ImportId) && Identity(row.SourceId) &&
        Identity(row.SourceVersionId) && Identity(row.SourceRowId, 512) && row.SourceContentHash is not null &&
        Regex.IsMatch(row.SourceContentHash, @"\A[a-f0-9]{64}\z", RegexOptions.CultureInvariant) &&
        row.PayerCompanyId != Guid.Empty && row.BeneficiaryCompanyId != Guid.Empty && Amount(row.PayAmt) && Count(row.PayCnt);
    public static bool Amount(string? raw) => raw is { Length: > 0 and <= MaximumDigits + 2 } &&
        raw.Count(c => c is >= '0' and <= '9') <= MaximumDigits &&
        Regex.IsMatch(raw, @"\A[+-]?[0-9]+(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant);
    public static bool Count(string? raw) => raw is { Length: > 0 and <= MaximumDigits } && raw.All(c => c is >= '0' and <= '9');
}
