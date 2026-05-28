using Hangfire.Console;
using Hangfire.Server;

namespace Fias.Service.Updater.Services.Progress;

/// <summary>Прогресс пишется в Console-вкладку текущей job'ы в Hangfire Dashboard.</summary>
public class HangfireConsoleProgressSink(PerformContext context) : IProgressSink
{
    public void WriteLine(string message) => context.WriteLine(message);

    public IJobProgressBar StartProgressBar(string title)
    {
        context.WriteLine(title);
        var bar = context.WriteProgressBar();
        return new HangfireBar(bar);
    }

    private sealed class HangfireBar(Hangfire.Console.Progress.IProgressBar inner) : IJobProgressBar
    {
        public void SetValue(double percent) => inner.SetValue(Math.Clamp(percent, 0, 100));
    }
}
