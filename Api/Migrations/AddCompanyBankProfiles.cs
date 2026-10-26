using Api.Resources;
using FluentMigrator;

namespace Api.Migrations;

[Migration(20261026001)]
public sealed class AddCompanyBankProfiles : Migration
{
    public override void Up() => Execute.Sql(CompanyBankSql.Schema);
    public override void Down() => Delete.Table("company_bank_profiles");
}
