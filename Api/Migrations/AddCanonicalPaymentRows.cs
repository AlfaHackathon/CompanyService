using Api.Resources;
using FluentMigrator;

namespace Api.Migrations;

[Migration(20261026002)]
public sealed class AddCanonicalPaymentRows : Migration
{
    public override void Up() => Execute.Sql(PaymentRowSql.Schema);
    public override void Down()
    {
        Delete.Table("company_payment_rows");
        Delete.Table("company_payment_source_versions");
    }
}
