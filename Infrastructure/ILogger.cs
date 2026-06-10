namespace VpnCheck.Infrastructure;

/// <summary>
/// Простой интерфейс для логирования
/// </summary>
public interface ILogger
{
    void LogInfo(string message);
    void LogSuccess(string message);
    void LogWarning(string message);
    void LogError(string message);
    void LogResult(string message);
}

