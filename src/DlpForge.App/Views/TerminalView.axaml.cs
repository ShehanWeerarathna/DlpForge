using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using DlpForge.App.ViewModels;

namespace DlpForge.App.Views;

public partial class TerminalView : UserControl
{
    public TerminalView()
    {
        InitializeComponent();
        CommandBox.AddHandler(KeyDownEvent, OnCommandKeyDown, RoutingStrategies.Tunnel);
        // Keep the newest output in view.
        OutputBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) OutputBox.CaretIndex = OutputBox.Text?.Length ?? 0;
        };
    }

    private TerminalViewModel? Vm => DataContext as TerminalViewModel;

    private void OnCommandKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is null) return;
        if (e.Key == Key.Up) { Vm.HistoryStep(-1); MoveCaretToEnd(); e.Handled = true; }
        else if (e.Key == Key.Down) { Vm.HistoryStep(1); MoveCaretToEnd(); e.Handled = true; }
    }

    private void MoveCaretToEnd() => CommandBox.CaretIndex = CommandBox.Text?.Length ?? 0;

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(OutputBox.Text ?? "");
    }
}
