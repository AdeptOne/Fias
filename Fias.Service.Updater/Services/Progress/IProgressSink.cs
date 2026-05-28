namespace Fias.Service.Updater.Services.Progress;

/// <summary>Куда писать прогресс — Hangfire.Console, ILogger или ничто (тесты).</summary>
public interface IProgressSink
{
    /// <summary>Произвольная строка лога.</summary>
    void WriteLine(string message);

    /// <summary>Создать progress-bar; вернуть «ручку» для обновления значения 0..100.</summary>
    IJobProgressBar StartProgressBar(string title);
}

public interface IJobProgressBar
{
    /// <summary>Установить значение progress-bar в пределах 0..100.</summary>
    void SetValue(double percent);
}
