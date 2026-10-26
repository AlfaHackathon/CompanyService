using Api.Resources;
using FluentMigrator;

namespace Api.Migrations;

[Migration(20261025001)]
public sealed class AddPublicKnowledgeOperations : Migration
{
    public override void Up() => Execute.Sql(PublicKnowledgeSql.Schema);
    public override void Down() => Delete.Table("company_public_knowledge_operations");
}
