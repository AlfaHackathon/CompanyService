using System.Data;
using System.Text.Json;
using Api.Common;
using Api.Contracts.Companies;
using Dapper;

namespace Api.Settings;

public sealed class CompanyBankDataTypeHandler : SqlMapper.TypeHandler<CompanyBankData>
{
    public override CompanyBankData Parse(object value) => value is string json
        ? JsonSerializer.Deserialize<CompanyBankData>(json, KnowledgeJson.Options) ?? throw new JsonException("Null canonical bank data payload.")
        : throw new JsonException("Canonical bank data must be a JSON object.");
    public override void SetValue(IDbDataParameter parameter, CompanyBankData? value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = value is null ? DBNull.Value : JsonSerializer.Serialize(value, KnowledgeJson.Options);
    }
}
