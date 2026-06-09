using Spectre.Console;

namespace VpnCheck.Infrastructure;

public sealed class ConsoleLogger(string logLevel = "error") : ILogger
{
    private readonly bool _showInfo    = logLevel is "all";
    private readonly bool _showWarning = logLevel is "all" or "warning";
    private readonly bool _showError   = logLevel is "all" or "warning" or "error";

    public void LogInfo(string message)
    {
        if (_showInfo)
            AnsiConsole.MarkupLine(Markup.Escape(message));
    }

    public void LogSuccess(string message)
    {
        if (_showInfo)
            AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(message)}");
    }

    public void LogWarning(string message)
    {
        if (_showWarning)
            AnsiConsole.MarkupLine($"[yellow]⚠[/] {Markup.Escape(message)}");
    }

    public void LogError(string message)
    {
        if (_showError)
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(message)}");
    }
}
