using System.Resources;

namespace Api.Resources;

public static class CompanyBankSql
{
    private static readonly ResourceManager Manager = new("Api.Resources.CompanyBankSql", typeof(CompanyBankSql).Assembly);
    public static string Schema => Get(nameof(Schema));
    public static string Latest => Get(nameof(Latest));
    public static string Versions => Get(nameof(Versions));
    public static string FindHash => Get(nameof(FindHash));
    public static string LatestRevision => Get(nameof(LatestRevision));
    public static string Insert => Get(nameof(Insert));
    private static string Get(string name) => Manager.GetString(name) ?? throw new InvalidOperationException("Missing CompanyBankSql." + name);
}
