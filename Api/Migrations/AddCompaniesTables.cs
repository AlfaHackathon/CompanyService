using FluentMigrator;

namespace Api.Migrations;

/// <summary>
/// Таблицы company-service: компании (с группами через parent_company_id),
/// отрасли (21 раздел ОКВЭД A–U, сид), связка m2m и участники компаний.
/// Инвайты живут в MongoDB (TTL), здесь их нет.
/// </summary>
[Migration(20261020001)]
public class AddCompaniesTables : Migration
{
    public override void Up()
    {
        Create.Table("companies")
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("name").AsString(256).NotNullable()
            .WithColumn("inn").AsString(12).Nullable()
            .WithColumn("creator_id").AsGuid().NotNullable()
            .WithColumn("parent_company_id").AsGuid().Nullable().ForeignKey("companies", "id")
            .WithColumn("region").AsString(128).Nullable()
            .WithColumn("revenue").AsDecimal(18, 2).Nullable()
            .WithColumn("count_of_employees").AsInt32().Nullable()
            .WithColumn("bank_profile_id").AsString(16).Nullable()   // SYNTHETIC_U (мост к graph-service)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable().WithDefault(SystemMethods.CurrentDateTimeOffset);

        Create.Table("industries")
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey()
            .WithColumn("code").AsString(1).NotNullable().Unique()   // буква раздела ОКВЭД
            .WithColumn("name").AsString(128).NotNullable();

        Create.Table("company_industries")
            .WithColumn("company_id").AsGuid().NotNullable().ForeignKey("companies", "id")
            .WithColumn("industry_id").AsGuid().NotNullable().ForeignKey("industries", "id");

        Create.PrimaryKey("pk_company_industries").OnTable("company_industries")
            .Columns("company_id", "industry_id");

        Create.Table("company_members")
            .WithColumn("company_id").AsGuid().NotNullable().ForeignKey("companies", "id")
            .WithColumn("user_id").AsGuid().NotNullable()
            .WithColumn("joined_at").AsDateTimeOffset().NotNullable().WithDefault(SystemMethods.CurrentDateTimeOffset);

        Create.PrimaryKey("pk_company_members").OnTable("company_members")
            .Columns("user_id", "company_id"); // у юзера не больше одной компании

        Create.Index("ix_companies_parent").OnTable("companies").OnColumn("parent_company_id");
        Create.Index("ix_companies_bank_profile").OnTable("companies").OnColumn("bank_profile_id");
        Create.Index("ix_company_members_company").OnTable("company_members").OnColumn("company_id");

        SeedIndustries();
    }

    public override void Down()
    {
        Delete.Table("company_members");
        Delete.Table("company_industries");
        Delete.Table("industries");
        Delete.Table("companies");
    }

    /// <summary>Сид 21 раздела ОКВЭД — та же разметка, по которой graph-service агрегирует отрасли.</summary>
    private void SeedIndustries()
    {
        var sections = new (string Code, string Name)[]
        {
            ("A", "Сельское, лесное хозяйство, охота, рыболовство"),
            ("B", "Добыча полезных ископаемых"),
            ("C", "Обрабатывающие производства"),
            ("D", "Электроэнергетика"),
            ("E", "Водоснабжение и канализация"),
            ("F", "Строительство"),
            ("G", "Торговля оптовая и розничная"),
            ("H", "Транспортировка и хранение"),
            ("I", "Гостиницы и общепит"),
            ("J", "Информация и связь"),
            ("K", "Финансовая и страховая деятельность"),
            ("L", "Операции с недвижимостью"),
            ("M", "Научная и техническая деятельность"),
            ("N", "Административная деятельность"),
            ("O", "Государственное управление"),
            ("P", "Образование"),
            ("Q", "Здравоохранение"),
            ("R", "Культура, спорт, досуг"),
            ("S", "Прочие услуги"),
            ("T", "Домашние хозяйства"),
            ("U", "Экстерриториальные организации"),
        };

        foreach (var (code, name) in sections)
        {
            Insert.IntoTable("industries")
                .Row(new { id = Guid.NewGuid(), code, name });
        }
    }
}
