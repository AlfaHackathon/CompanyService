using System.Text.Json;
using Api.Common;
using Api.Contracts.Payments;
using Xunit;

public sealed class PaymentRowValidationTests
{
    internal static PaymentRowInput Row() => new("synthetic-import", "synthetic-logical-source", "version-1", new string('a', 64), "part-A:row-1",
        Guid.Parse("11111111-1111-4111-8111-111111111111"), Guid.Parse("22222222-2222-4222-8222-222222222222"), "0001118581311000.000000000001", "000124671");

    [Fact]
    public void CompleteSourceAndMetadataRoundTripWithoutNumericConversionOrPairDeduplication()
    {
        var row = Row();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(row, options);
        Assert.Equal(9, JsonSerializer.Deserialize<JsonElement>(json).EnumerateObject().Count());
        Assert.Equal(row, JsonSerializer.Deserialize<PaymentRowInput>(json, options));
        Assert.True(PaymentRowValidation.Valid(row));
        Assert.True(PaymentRowValidation.Valid(row with { SourceRowId = "part-B:row-1" }));
        Assert.DoesNotContain("currency", json);
        Assert.DoesNotContain("year", json);
    }

    [Theory]
    [InlineData("-0.123456789012345678901234567890", "0", true)]
    [InlineData("+001.00", "0001", true)]
    [InlineData("0", "0", true)]
    [InlineData("1e9", "1", false)]
    [InlineData("1,23", "1", false)]
    [InlineData("1000.0\n", "1", false)]
    [InlineData("1000.0\r\n", "1", false)]
    [InlineData("1000.0 ", "1", false)]
    [InlineData("", "1", false)]
    [InlineData("1", "-1", false)]
    [InlineData("1", "1.0", false)]
    [InlineData("1", "1e2", false)]
    [InlineData("1", "", false)]
    public void ExactLexicalPolicyIsIndependentOfObservedSampleMinimaMaximaAndScale(string amount, string count, bool valid) =>
        Assert.Equal(valid, PaymentRowValidation.Valid(Row() with { PayAmt = amount, PayCnt = count }));

    [Fact]
    public void DigitBoundsAreExplicitRequestResourceLimitsAndMetadataIsOpaque()
    {
        Assert.True(PaymentRowValidation.Valid(Row() with { PayAmt = new string('9', 4096), PayCnt = new string('9', 4096) }));
        Assert.False(PaymentRowValidation.Valid(Row() with { PayAmt = new string('9', 4097) }));
        Assert.False(PaymentRowValidation.Valid(Row() with { PayCnt = new string('9', 4097) }));
        Assert.True(PaymentRowValidation.Valid(Row() with { SourceRowId = "opaque-part/name:row+00001", SourceId = "opaque/source identity" }));
        Assert.False(PaymentRowValidation.Valid(Row() with { SourceContentHash = "invented-non-hash" }));
        Assert.False(PaymentRowValidation.Valid(Row() with { PayerCompanyId = Guid.Empty }));
        Assert.False(PaymentRowValidation.Valid(Row() with { SourceContentHash = new string('a', 64) + "\n" }));
        Assert.False(PaymentRowValidation.Valid(Row() with { SourceContentHash = new string('a', 64) + "\r\n" }));
    }

    [Fact]
    public void UnknownFieldsAndNumericJsonAmountsAreRejectedInsteadOfSilentlyDroppingSourceValues()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(Row(), options);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PaymentRowInput>(json[..^1] + ",\"unknownColumn\":\"value\"}", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PaymentRowInput>(json.Replace("\"000124671\"", "124671", StringComparison.Ordinal), options));
    }
}
