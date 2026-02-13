using NuGet.Common;

public class ConsoleLogger : LoggerBase
{
    public static readonly ConsoleLogger Instance = new();

    public override void Log(ILogMessage message)
    {
        Console.WriteLine($"    {message.Message}");
    }

    public override Task LogAsync(ILogMessage message)
    {
        Log(message);
        return Task.CompletedTask;
    }
}