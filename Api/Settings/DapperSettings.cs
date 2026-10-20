using AlfaHackaton.DapperContext.Common.Interfaces.Dapper.Settings;

namespace Api.Settings;

/// <summary>
/// Настройки подключения к Postgres для DapperContext.
/// </summary>
public class DapperSettings : IDapperSettings
{
    public DapperSettings(string connectionString)
    {
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; set; }
}
