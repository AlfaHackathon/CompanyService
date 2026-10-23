using Api.Resources;
using FluentMigrator;
namespace Api.Migrations;

[Migration(20261024001)]
public sealed class AddKnowledgeRevisions : Migration
{
    public override void Up()
    {
        Execute.Sql(KnowledgeSql.KnowledgeSchema);
        Execute.Sql(KnowledgeSql.ChangeSchema);
    }
    public override void Down()
    {
        Delete.Table("company_knowledge_changes");
        Delete.Table("company_knowledge_revisions");
    }
}
