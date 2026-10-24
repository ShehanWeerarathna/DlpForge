using Avalonia.Controls;
using Avalonia.Input;
using DlpForge.App.ViewModels;

namespace DlpForge.App.Views;

public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is HistoryViewModel vm && vm.Selected != null)
            vm.OpenFileCommand.Execute(vm.Selected);
    }
}
