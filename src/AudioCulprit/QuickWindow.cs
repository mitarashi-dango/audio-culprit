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
        Title = "今の音？ · AudioCulprit";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = System.Windows.Application.Current.MainWindow?.Icon;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = monitoring ? "今の音？" : "監視停止中 · 保存済みの音", Foreground = Brushes.Teal, FontSize = 16 });
        panel.Children.Add(new TextBlock { Text = item?.DisplayName ?? "まだ音の記録がありません", FontSize = 26, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        if (item != null)
        {
            panel.Children.Add(new TextBlock { Text = item.SourceLabel, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var seconds = Math.Max(0, (DateTime.UtcNow - item.StartTimeUtc).TotalSeconds);
            var ago = seconds < 60 ? $"{seconds:0}秒前" : seconds < 3600 ? $"{seconds / 60:0}分前" : $"{seconds / 3600:0}時間前";
            panel.Children.Add(new TextBlock { Text = $"{ago} · {item.DurationLabel} · {item.StateLabel}\n{item.DeviceName}", TextWrapping = TextWrapping.Wrap });
            var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
            var detailButton = new Button { Content = "詳細" };
            detailButton.Click += (_, _) => { Close(); details(item); };
            var ignoreButton = new Button { Content = "このアプリを無視" };
            ignoreButton.Click += (_, _) => { try { ignore(item); Close(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存できませんでした"); } };
            actions.Children.Add(detailButton);
            actions.Children.Add(ignoreButton);
            panel.Children.Add(actions);
        }
        var close = new Button { Content = "閉じる（Esc）", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = panel;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
}
