using System.Resources;

namespace Api.Resources;

public static class PaymentRowSql
{
    private static readonly ResourceManager Manager = new("Api.Resources.PaymentRowSql", typeof(PaymentRowSql).Assembly);
    public static string Schema => Get(nameof(Schema));
    public static string FindVersionHash => Get(nameof(FindVersionHash));
    public static string FindRowHash => Get(nameof(FindRowHash));
    public static string ReadRow => Get(nameof(ReadRow));
    public static string LastKnownTicks => Get(nameof(LastKnownTicks));
    public static string InsertVersion => Get(nameof(InsertVersion));
    public static string InsertRow => Get(nameof(InsertRow));
    public static string Activity => Get(nameof(Activity));
    private static string Get(string name) => Manager.GetString(name) ?? throw new InvalidOperationException("Missing PaymentRowSql." + name);
}
