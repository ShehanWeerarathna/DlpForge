using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DlpForge.App.Views;

/// <summary>Asks when a queued download should start. A null At means "start now".</summary>
public partial class ScheduleWindow : Window
{
    public sealed record Result(DateTimeOffset? At);

    public ScheduleWindow() => InitializeComponent();

    public static Task<Result?> Ask(Window owner, string title, DateTimeOffset? current)
    {
        // Default: the current schedule, or the next full hour.
        var next = DateTime.Now.AddHours(1);
        var start = current?.ToLocalTime().DateTime ?? new DateTime(next.Year, next.Month, next.Day, next.Hour, 0, 0);
        var w = new ScheduleWindow();
        w.ItemTitle.Text = title;
        w.DateBox.SelectedDate = new DateTimeOffset(start.Date);
        w.TimeBox.SelectedTime = start.TimeOfDay;
        return w.ShowDialog<Result?>(owner);
    }

    private void OnSchedule(object? sender, RoutedEventArgs e)
    {
        if (DateBox.SelectedDate is not { } date || TimeBox.SelectedTime is not { } time)
        {
            ShowError("Pick a date and a time.");
            return;
        }
        var local = date.Date + time;
        var at = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        if (at <= DateTimeOffset.Now)
        {
            ShowError("That time has passed. Pick a later time, or use Start now.");
            return;
        }
        Close(new Result(at));
    }

    private void OnStartNow(object? sender, RoutedEventArgs e) => Close(new Result(null));

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
