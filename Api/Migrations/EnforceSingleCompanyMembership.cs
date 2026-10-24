using Api.Resources;
using FluentMigrator;

namespace Api.Migrations;

[Migration(20261024002)]
public sealed class EnforceSingleCompanyMembership : Migration
{
    public override void Up()
    {
        Execute.Sql(CompanySql.EnforceSingleMembership);
    }

    public override void Down()
    {
        Delete.UniqueConstraint("uq_company_members_user").FromTable("company_members");
    }
}
