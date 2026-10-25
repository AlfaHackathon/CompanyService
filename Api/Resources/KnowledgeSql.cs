using System.Resources;
namespace Api.Resources;

public static class KnowledgeSql
{
    private static readonly ResourceManager Manager = new("Api.Resources.KnowledgeSql", typeof(KnowledgeSql).Assembly);
    public static string ChangeSchema => Manager.GetString(nameof(ChangeSchema)) ?? throw new InvalidOperationException("Missing KnowledgeSql.ChangeSchema");
    public static string AppendChange => Manager.GetString(nameof(AppendChange)) ?? throw new InvalidOperationException("Missing KnowledgeSql.AppendChange");
    public static string ReadNextChange => Manager.GetString(nameof(ReadNextChange)) ?? throw new InvalidOperationException("Missing KnowledgeSql.ReadNextChange");
    public static string KnowledgeSchema => Manager.GetString(nameof(KnowledgeSchema)) ?? throw new InvalidOperationException("Missing KnowledgeSql.KnowledgeSchema");
    public static string FindKnowledgeHash => Manager.GetString(nameof(FindKnowledgeHash)) ?? throw new InvalidOperationException("Missing KnowledgeSql.FindKnowledgeHash");
    public static string InsertKnowledge => Manager.GetString(nameof(InsertKnowledge)) ?? throw new InvalidOperationException("Missing KnowledgeSql.InsertKnowledge");
    public static string LatestKnowledge => Manager.GetString(nameof(LatestKnowledge)) ?? throw new InvalidOperationException("Missing KnowledgeSql.LatestKnowledge");
    public static string AllKnowledge => Manager.GetString(nameof(AllKnowledge)) ?? throw new InvalidOperationException("Missing KnowledgeSql.AllKnowledge");
    public static string FindKnowledgeAt => Manager.GetString(nameof(FindKnowledgeAt)) ?? throw new InvalidOperationException("Missing KnowledgeSql.FindKnowledgeAt");
    public static string WriteLock => Manager.GetString(nameof(WriteLock)) ?? throw new InvalidOperationException("Missing KnowledgeSql.WriteLock");
}
