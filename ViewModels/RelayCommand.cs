using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartphoneMonitor.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Func<object?, Task>? _async;
        private readonly Action<object?>? _sync;
        private readonly Func<object?, bool>? _can;

        public event EventHandler? CanExecuteChanged;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _sync = execute;
            _can = canExecute;
        }

        public RelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
        {
            _async = execute;
            _can = canExecute;
        }

        public bool CanExecute(object? p)
        {
            return _can?.Invoke(p) ?? true;
        }

        public async void Execute(object? p)
        {
            if (_async != null)
            {
                await _async(p);
            }
            else
            {
                _sync?.Invoke(p);
            }
        }

        public void Raise()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public RelayCommand(Action<T?> execute)
        {
            _execute = execute;
        }

        public bool CanExecute(object? p)
        {
            return true;
        }

        public void Execute(object? p)
        {
            _execute((T?)p);
        }
    }
}
