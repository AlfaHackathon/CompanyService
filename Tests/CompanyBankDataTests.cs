using System.Text.Json;
using System.Data;
using Api.Common;
using Api.Contracts.Companies;
using Api.Models;
using Api.Settings;
using Dapper;
using Xunit;

public sealed class CompanyBankDataTests
{
    private static readonly Guid Group = Guid.Parse("33333333-3333-4333-8333-333333333333");
    public static CompanyBankData AllFields() => new()
    {
        OrgType = "synthetic-org",
        OkfsName = "synthetic-ownership",
        CompanyAgeYearsGradation = "3–5",
        EcoMarkLabel = "opaque-ECO",
        MainOkvedCode = "01.11.01",
        MainOkvedName = "synthetic-activity",
        NegativeMark = "N-unknown",
        AmountWorkersFnsGradation = "11–20",
        AmountWorkersSparkGradation = "21–30",
        ClType = "opaque-client",
        ClProductRkoMark = "01",
        ClProductGarMark = "0",
        ClProductLoaMark = "Y",
        ClProductLeaMark = "N",
        ClProductDepMark = "true-as-source-string",
        ClProductZppMark = "",
        FtsFlag = "opaque-FTS",
        FtsCountryName = "synthetic-country",
        FactoringMark = "opaque-factoring",
        RevenueCompany = "000123456789012345678901234567890.12345678901234567890",
        GroupId = Group,
        City = "synthetic-city",
        Region = "synthetic-region"
    };

    [Fact]
    public void CompleteConfirmedSourceContractRoundTripsWithoutCoercionOrPrecisionLoss()
    {
        var data = AllFields();
        var json = JsonSerializer.Serialize(data, KnowledgeJson.Options);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(23, element.EnumerateObject().Count());
        Assert.Equal(data.RevenueCompany, element.GetProperty("revenueCompany").GetString());
        Assert.Equal("01.11.01", element.GetProperty("mainOkvedCode").GetString());
        Assert.Equal("01", element.GetProperty("clProductRkoMark").GetString());
        Assert.Equal("", element.GetProperty("clProductZppMark").GetString());
        Assert.Equal(Group, element.GetProperty("groupId").GetGuid());
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<CompanyBankData>(json, KnowledgeJson.Options), KnowledgeJson.Options));
        Assert.DoesNotContain("parentCompanyId", json);
        Assert.DoesNotContain("currency", json);
        Assert.DoesNotContain("period", json);
        Assert.DoesNotContain("syntheticU", json);
    }

    [Fact]
    public void MissingSourceValuesStayNullAndNoRawSourceKeyOrEnumIsGuessed()
    {
        var element = JsonSerializer.SerializeToElement(new CompanyBankData(), KnowledgeJson.Options);
        Assert.All(element.EnumerateObject(), property => Assert.Equal(JsonValueKind.Null, property.Value.ValueKind));
        Assert.Equal(23, element.EnumerateObject().Count());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("000123456789012345678901234567890.12345678901234567890", true)]
    [InlineData("-001.2300", true)]
    [InlineData("0", true)]
    [InlineData("", true)]
    [InlineData(" ", true)]
    [InlineData("\t  ", true)]
    [InlineData("1e6", false)]
    [InlineData("1,23", false)]
    [InlineData("NaN", false)]
    public void RevenueValidationRetainsExactRepresentationAndRejectsMalformedInputWithoutGuessing(string? value, bool valid) =>
        Assert.Equal(valid, CompanyBankDataValidation.Valid(new() { RevenueCompany = value }));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t  ")]
    public void UnknownRawRevenueBlanksRemainExactStringsWithoutBecomingNumericOrNull(string raw)
    {
        var data = new CompanyBankData { RevenueCompany = raw };
        var json = JsonSerializer.Serialize(data, KnowledgeJson.Options);
        Assert.True(CompanyBankDataValidation.Valid(data));
        Assert.Equal(raw, JsonSerializer.Deserialize<CompanyBankData>(json, KnowledgeJson.Options)!.RevenueCompany);
        Assert.Equal(JsonValueKind.String, JsonSerializer.Deserialize<JsonElement>(json).GetProperty("revenueCompany").ValueKind);
    }

    [Fact]
    public void RepositoryProjectionMaterializesTypedJsonAndKeepsLegacySqlNullAsNull()
    {
        SqlMapper.AddTypeHandler(new CompanyBankDataTypeHandler());
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        using var table = new DataTable();
        table.Columns.Add("id", typeof(Guid));
        table.Columns.Add("name", typeof(string));
        table.Columns.Add("creator_id", typeof(Guid));
        table.Columns.Add("created_at", typeof(DateTime));
        table.Columns.Add("bank_data", typeof(string));
        var expected = AllFields();
        table.Rows.Add(Guid.NewGuid(), "Synthetic operative company", Guid.NewGuid(), DateTime.UtcNow, JsonSerializer.Serialize(expected, KnowledgeJson.Options));
        table.Rows.Add(Guid.NewGuid(), "Synthetic legacy company", Guid.NewGuid(), DateTime.UtcNow, DBNull.Value);
        using var reader = table.CreateDataReader();
        var parse = reader.GetRowParser<Company>();
        Assert.True(reader.Read());
        Assert.Equal(expected, parse(reader).BankData);
        Assert.True(reader.Read());
        Assert.Null(parse(reader).BankData);
    }
}
