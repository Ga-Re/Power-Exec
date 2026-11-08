using System.Windows.Input;

namespace PowerExec.Infrastructure;

/// <summary>Bindet <see cref="RelayCommand"/> an den CommandManager von WPF (schwache Handler, Neuprüfung bei Fokus/Eingabe).</summary>
public sealed class WpfCommandRequery : ICommandRequeryBackend
{
    public void Add(EventHandler handler) => CommandManager.RequerySuggested += handler;

    public void Remove(EventHandler handler) => CommandManager.RequerySuggested -= handler;

    public void Invalidate() => CommandManager.InvalidateRequerySuggested();
}
