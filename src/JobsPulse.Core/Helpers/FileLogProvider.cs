using Vostok.Logging.File;
using Vostok.Logging.File.Configuration;

namespace JobsPulse.Core.Helpers;

public static class FileLogProvider
{
    public static FileLog Create(string logPrefix)
    {
        return new FileLog(new FileLogSettings()
        {
            RollingStrategy = new RollingStrategyOptions()
            {
                Period = RollingPeriod.Day,
                Type = RollingStrategyType.ByTime,
                MaxFiles = 7
            },
            FileOpenMode = FileOpenMode.Append,
            FilePath = $"logs/{logPrefix}",
        });
    }
}