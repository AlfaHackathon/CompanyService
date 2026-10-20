using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Api.Models;

/// <summary>
/// Инвайт в компанию. Живёт в MongoDB: TTL-индекс по ExpiresAt удаляет протухшие
/// инвайты автоматически (проверка Mongo ~раз в минуту), уникальный индекс — на Code.
/// </summary>
public class InviteDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid CompanyId { get; set; }

    /// <summary>Короткий код для инвайт-ссылки.</summary>
    public string Code { get; set; } = null!;

    [BsonRepresentation(BsonType.String)]
    public Guid CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Поле TTL-индекса: Mongo удалит документ, когда это время пройдёт.</summary>
    public DateTime ExpiresAt { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? UsedBy { get; set; }

    public DateTime? UsedAt { get; set; }
}
