namespace VpnConfigTester.Infrastructure;

/// <summary>
/// Репортер прогресса для консоли
/// </summary>
public sealed class ConsoleProgressReporter
{
    private int _lastReported = -1;

    public void Report(int tested, int total, int successful)
    {
        // Обновляем прогресс каждые 10% или при завершении
        var percentage = (int)((double)tested / total * 100);
        var shouldReport = tested % 10 == 0 || tested == total || percentage % 10 == 0;

        if (shouldReport && _lastReported != percentage)
        {
            Console.Write($"\rТестировано: {tested}/{total} ({percentage}%) | Успешных: {successful}");
            _lastReported = percentage;
        }

        if (tested == total)
        {
            Console.WriteLine(); // Новая строка после завершения
        }
    }
}

