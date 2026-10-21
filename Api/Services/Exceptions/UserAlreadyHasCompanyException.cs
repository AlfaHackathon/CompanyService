namespace Api.Services.Exceptions;

/// <summary>Пользователь уже состоит в компании — создание второй запрещено.</summary>
public class UserAlreadyHasCompanyException(Guid companyId)
    : InvalidOperationException($"Пользователь уже состоит в компании {companyId}")
{
    /// <summary>Id компании, в которой уже состоит пользователь.</summary>
    public Guid CompanyId { get; } = companyId;
}
