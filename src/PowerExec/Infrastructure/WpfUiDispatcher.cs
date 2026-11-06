using System.Windows.Threading;

namespace PowerExec.Infrastructure;

/// <summary><see cref="IUiDispatcher"/> für WPF.</summary>
public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action) => dispatcher.BeginInvoke(action);

    public IUiTimer CreateTimer(TimeSpan interval, Action tick) => new Timer(dispatcher, interval, tick);

    private sealed class Timer : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public Timer(Dispatcher dispatcher, TimeSpan interval, Action tick)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = interval };
            _timer.Tick += (_, _) => tick();
        }

        public bool IsRunning => _timer.IsEnabled;
        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
    }
}
