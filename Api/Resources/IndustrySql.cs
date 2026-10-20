using System.Resources;

namespace Api.Resources;

/// <summary>
/// Доступ к SQL-запросам из Resources/IndustrySql.resx.
/// Весь SQL живёт в ресурсах, репозитории не содержат сырых строк.
/// </summary>
public static class IndustrySql
{
    private static readonly ResourceManager Manager =
        new("Api.Resources.IndustrySql", typeof(IndustrySql).Assembly);

    public static string GetAll { get; } = Manager.GetString(nameof(GetAll))
        ?? throw new InvalidOperationException("Ресурс IndustrySql.GetAll не найден");

    public static string GetByCodes { get; } = Manager.GetString(nameof(GetByCodes))
        ?? throw new InvalidOperationException("Ресурс IndustrySql.GetByCodes не найден");
}
