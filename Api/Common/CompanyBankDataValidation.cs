using System.Text.RegularExpressions;
using Api.Contracts.Companies;

namespace Api.Common;

public static class CompanyBankDataValidation
{
    // Validate representation without numeric conversion, rounding, or interpreting source flags.
    public static bool Valid(CompanyBankData? data) => data is not null && data.GroupId != Guid.Empty &&
        (string.IsNullOrWhiteSpace(data.RevenueCompany) || Regex.IsMatch(data.RevenueCompany, @"^-?[0-9]+(?:\.[0-9]+)?$", RegexOptions.CultureInvariant));
}
