namespace VpnCheck.Infrastructure;

/// <summary>
/// Null Object для ILogger — безопасный заменитель null, не требует null-проверок в сервисах.
/// </summary>
public sealed class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();

    private NullLogger() { }

    public void LogInfo(string message) { }
    public void LogSuccess(string message) { }
    public void LogWarning(string message) { }
    public void LogError(string message) { }
    public void LogResult(string message) { }
}
