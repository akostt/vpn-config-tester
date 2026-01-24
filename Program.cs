using VpnConfigTester;
using VpnConfigTester.Infrastructure;
using VpnConfigTester.Models;
using VpnConfigTester.Services;

// Создаем конфигурацию
var config = new ApplicationConfiguration();

// Создаем зависимости
var logger = new ConsoleLogger();
var configDownloader = new ConfigDownloader(config, logger);
var serverParser = new ServerParser(logger);
var serverTester = new ServerTester(config, logger);
var dnsResolver = new DnsResolverService(logger);
var configWriter = new ConfigWriter(logger);
var ipRangeAnalyzer = new IpRangeAnalyzerService(config, logger);

// Создаем приложение
var app = new Application(
    config,
    configDownloader,
    serverParser,
    serverTester,
    configWriter,
    ipRangeAnalyzer,
    dnsResolver,
    logger);

// Обработка аргументов командной строки
if (args.Length > 0 && (args[0] == "--analyze" || args[0] == "-a"))
{
    app.AnalyzeExistingData();
    Console.WriteLine("\nНажмите любую клавишу для выхода...");
    try
    {
        Console.ReadKey(true);
    }
    catch (InvalidOperationException)
    {
        // Консоль недоступна, просто выходим
    }
    return;
}

// Проверяем флаг --skip-download или --local
var skipDownload = args.Length > 0 && 
    (args[0] == "--skip-download" || args[0] == "--local" || args[0] == "-l");

// Запуск основного процесса
try
{
    await app.RunAsync(skipDownload);
}
catch (Exception ex)
{
    logger.LogError($"Критическая ошибка: {ex.Message}");
    if (ex.StackTrace != null)
    {
        logger.LogError($"Stack trace: {ex.StackTrace}");
    }
    Environment.ExitCode = 1;
}
finally
{
    Console.WriteLine();
    Console.WriteLine("Нажмите любую клавишу для выхода...");
    try
    {
        Console.ReadKey(true);
    }
    catch (InvalidOperationException)
    {
        // Консоль недоступна, просто выходим
    }
}
