using Api.Contracts.Companies;
using Api.Models;

namespace Api.Services.Interfaces;

/// <summary>Бизнес-операции над компаниями. Контроллеры работают только с сервисом.</summary>
public interface ICompanyService
{
    /// <summary>Компания пользователя (участие), null — не состоит.</summary>
    Task<Company?> GetByUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Компания по id.</summary>
    Task<Company?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Ответ-контракт компании с отраслями и флагом «я создатель».</summary>
    Task<CompanyResponse> ToResponseAsync(Company company, Guid requesterId, CancellationToken ct = default);

    /// <summary>Частичное обновление. false — компания не найдена.</summary>
    Task<bool> UpdateAsync(Guid id, UpdateCompanyRequest patch, CancellationToken ct = default);

    /// <summary>Заменить отрасли компании по кодам ОКВЭД.
    /// ok=false — часть кодов неизвестна, validCodes содержит найденные.</summary>
    Task<(bool Ok, List<string> ValidCodes)> SetIndustriesByCodesAsync(
        Guid companyId, IReadOnlyCollection<string> codes, CancellationToken ct = default);

    /// <summary>Участники компании.</summary>
    Task<List<MemberResponse>> GetMembersAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>internal: id участников компании (для фильтрации списков user-service).</summary>
    Task<List<Guid>> GetMemberIdsAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>Дочерние компании.</summary>
    Task<List<Company>> GetChildrenAsync(Guid parentId, CancellationToken ct = default);

    /// <summary>internal: создать компанию при регистрации без инвайта (+ отрасли).
    /// Создатель автоматически добавляется участником. Если у создателя уже есть компания —
    /// бросает <see cref="UserAlreadyHasCompanyException"/>.</summary>
    Task<Company> CreateInternalAsync(CreateCompanyInternalRequest request, CancellationToken ct = default);

    /// <summary>internal: добавить участника в компанию.</summary>
    Task AddMemberAsync(Guid companyId, Guid userId, CancellationToken ct = default);
}
