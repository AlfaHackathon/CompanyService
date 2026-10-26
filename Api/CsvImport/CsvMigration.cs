using FluentMigrator;
namespace Api.CsvImport;
[Migration(20261026003)]
public sealed class CsvMigration : Migration
{
    public override void Up() => Execute.Sql(CsvSql.Schema);
    public override void Down() => throw new NotSupportedException("CSV lineage is durable; rollback requires an explicit data-preservation plan.");
}
