using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AudioCulprit.Core;

namespace AudioCulprit;

internal sealed class QuickWindow : Window
{
    public QuickWindow(AudioEvent? item, bool monitoring, Action<AudioEvent> details, Action<AudioEvent> ignore)
    {
        Title = UiText.Get("QuickTitle");
        Width = 430;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = System.Windows.Application.Current.MainWindow?.Icon;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = monitoring ? UiText.Get("RecentSound") : UiText.Get("QuickPaused"), Foreground = Brushes.Teal, FontSize = 16 });
        panel.Children.Add(new TextBlock { Text = item?.DisplayName ?? UiText.Get("NoHistory"), FontSize = 26, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        if (item != null)
        {
            panel.Children.Add(new TextBlock { Text = item.SourceLabel, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var seconds = Math.Max(0, (DateTime.UtcNow - item.StartTimeUtc).TotalSeconds);
            var ago = seconds < 60 ? UiText.Format("SecondsAgo", seconds) : seconds < 3600 ? UiText.Format("MinutesAgo", seconds / 60) : UiText.Format("HoursAgo", seconds / 3600);
            panel.Children.Add(new TextBlock { Text = $"{ago} · {item.DurationLabel} · {item.StateLabel}\n{item.DeviceName}", TextWrapping = TextWrapping.Wrap });
            var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
            var detailButton = new Button { Content = UiText.Get("Details") };
            detailButton.Click += (_, _) => { Close(); details(item); };
            var ignoreButton = new Button { Content = UiText.Get("IgnoreApp") };
            ignoreButton.Click += (_, _) => { try { ignore(item); Close(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, UiText.Get("SaveFailed")); } };
            actions.Children.Add(detailButton);
            actions.Children.Add(ignoreButton);
            panel.Children.Add(actions);
        }
        var close = new Button { Content = UiText.Get("Close"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = panel;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
}
