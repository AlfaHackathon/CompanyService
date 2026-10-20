namespace Api.Models;

/// <summary>
/// Отрасль («то, чем занимается компания»). Сидируется 21 разделом ОКВЭД (A–U) —
/// та же разметка, по которой graph-service агрегирует экспозицию контрагентов.
/// </summary>
public class Industry
{
    public required Guid Id { get; set; }

    /// <summary>Буква раздела ОКВЭД: 'G' — торговля и т.д.</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }
}
