using System.Resources;

namespace Api.Resources;

/// <summary>
/// Доступ к SQL-запросам из Resources/CompanySql.resx.
/// Весь SQL живёт в ресурсах, репозитории не содержат сырых строк.
/// </summary>
public static class CompanySql
{
    private static readonly ResourceManager Manager =
        new("Api.Resources.CompanySql", typeof(CompanySql).Assembly);

    public static string GetById { get; } = Manager.GetString(nameof(GetById))
        ?? throw new InvalidOperationException("Ресурс CompanySql.GetById не найден");

    public static string GetByUser { get; } = Manager.GetString(nameof(GetByUser))
        ?? throw new InvalidOperationException("Ресурс CompanySql.GetByUser не найден");

    public static string Create { get; } = Manager.GetString(nameof(Create))
        ?? throw new InvalidOperationException("Ресурс CompanySql.Create не найден");

    public static string Update { get; } = Manager.GetString(nameof(Update))
        ?? throw new InvalidOperationException("Ресурс CompanySql.Update не найден");

    public static string SetIndustriesDelete { get; } = Manager.GetString(nameof(SetIndustriesDelete))
        ?? throw new InvalidOperationException("Ресурс CompanySql.SetIndustriesDelete не найден");

    public static string SetIndustriesInsert { get; } = Manager.GetString(nameof(SetIndustriesInsert))
        ?? throw new InvalidOperationException("Ресурс CompanySql.SetIndustriesInsert не найден");

    public static string GetIndustries { get; } = Manager.GetString(nameof(GetIndustries))
        ?? throw new InvalidOperationException("Ресурс CompanySql.GetIndustries не найден");

    public static string GetMembers { get; } = Manager.GetString(nameof(GetMembers))
        ?? throw new InvalidOperationException("Ресурс CompanySql.GetMembers не найден");

    public static string GetChildren { get; } = Manager.GetString(nameof(GetChildren))
        ?? throw new InvalidOperationException("Ресурс CompanySql.GetChildren не найден");

    public static string ReserveMember { get; } = Manager.GetString(nameof(ReserveMember))
        ?? throw new InvalidOperationException("Ресурс CompanySql.ReserveMember не найден");

    public static string EnforceSingleMembership { get; } = Manager.GetString(nameof(EnforceSingleMembership))
        ?? throw new InvalidOperationException("Ресурс CompanySql.EnforceSingleMembership не найден");
}
