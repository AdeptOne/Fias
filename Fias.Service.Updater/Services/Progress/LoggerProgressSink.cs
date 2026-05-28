namespace Fias.Service.Updater.Services.Progress;

/// <summary>Fallback: прогресс просто пишется в ILogger (job-контекст не задан).</summary>
public class LoggerProgressSink(ILogger logger) : IProgressSink
{
    public void WriteLine(string message) => logger.LogInformation("{Message}", message);

    public IJobProgressBar StartProgressBar(string title)
    {
        logger.LogInformation("{Title}", title);
        return new LoggerBar(logger, title);
    }

    private sealed class LoggerBar(ILogger logger, string title) : IJobProgressBar
    {
        private int _lastBucket = -1;

        public void SetValue(double percent)
        {
            // Логируем не чаще, чем каждые 10%, чтобы не засорять stdout.
            var bucket = (int)(Math.Clamp(percent, 0, 100) / 10);
            if (bucket == _lastBucket) return;
            _lastBucket = bucket;
            logger.LogInformation("{Title}: {Percent:F0}%", title, percent);
        }
    }
}
