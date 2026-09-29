using System;
using System.Windows.Input;

namespace TradingBacktest.Mvvm
{
    /// <summary>
    /// A simple ICommand over a delegate. Hand-written for the same toolchain reason as
    /// <see cref="ObservableObject"/> - no generator or package reference involved.
    ///
    /// Supports both parameterless handlers (Start, Browse) and handlers that read the
    /// command parameter (the period presets).
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute)
            : this(Wrap(execute), null)
        {
        }

        public RelayCommand(Action<object> execute)
            : this(execute, null)
        {
        }

        public RelayCommand(Action execute, Func<bool> canExecute)
            : this(Wrap(execute), canExecute)
        {
        }

        public RelayCommand(Action<object> execute, Func<bool> canExecute)
        {
            if (execute == null)
            {
                throw new ArgumentNullException("execute");
            }

            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute();
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }

        private static Action<object> Wrap(Action execute)
        {
            if (execute == null)
            {
                throw new ArgumentNullException("execute");
            }

            return delegate { execute(); };
        }
    }
}
