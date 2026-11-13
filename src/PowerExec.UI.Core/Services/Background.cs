using System.Runtime.CompilerServices;

namespace PowerExec.UI.Services;

/// <summary>
/// Aufgaben, auf die niemand wartet (Einlesen, Prüfungen, Aktualisierungen im Hintergrund). Scheitert eine, steht das
/// im Protokoll – statt dass die Ausnahme ungesehen verschwindet.
/// </summary>
public static class Background
{
    /// <summary>Aufgabe laufen lassen, ohne auf sie zu warten; ein Fehler wird protokolliert.</summary>
    /// <param name="task">Die laufende Aufgabe.</param>
    /// <param name="what">Woran man sie im Protokoll erkennt (ohne Angabe: der Ausdruck des Aufrufs).</param>
    public static void Forget(this Task task, [CallerArgumentExpression(nameof(task))] string? what = null)
    {
        if (task.IsCompletedSuccessfully) return;
        task.ContinueWith(static (t, state) => Report(t.Exception, (string?)state), what,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Arbeit auf einem Hintergrund-Thread erledigen, ohne auf sie zu warten; ein Fehler wird protokolliert.</summary>
    public static void Run(Action work, [CallerArgumentExpression(nameof(work))] string? what = null) => Task.Run(work).Forget(what);

    private static void Report(AggregateException? error, string? what)
    {
        if (error == null) return;
        // Der Ausdruck kann ein mehrzeiliger Lambda-Ausdruck sein: für das Protokoll genügt der Anfang
        var name = (what ?? "task").ReplaceLineEndings(" ");
        if (name.Length > 80) name = name[..80] + "…";
        AppLog.Error($"Background task failed ({name})", error.GetBaseException());
    }
}
