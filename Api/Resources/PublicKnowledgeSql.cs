using System.Resources;

namespace Api.Resources;

public static class PublicKnowledgeSql
{
    private static readonly ResourceManager Manager = new("Api.Resources.PublicKnowledgeSql", typeof(PublicKnowledgeSql).Assembly);
    private static string Required(string name) => Manager.GetString(name) ?? throw new InvalidOperationException("Missing PublicKnowledgeSql." + name);
    public static string Schema => Required(nameof(Schema));
    public static string ReservationLock => Required(nameof(ReservationLock));
    public static string FindOperation => Required(nameof(FindOperation));
    public static string LastCompanyTime => Required(nameof(LastCompanyTime));
    public static string InsertOperation => Required(nameof(InsertOperation));
    public static string CompleteOperation => Required(nameof(CompleteOperation));
}
