using System.Windows;
using System.Windows.Controls;

namespace ReportManagement.Helpers;

internal static class InputDialog
{
    public static string? Show(string title, string message, string defaultValue)
    {
        var textBox = new TextBox
        {
            Text = defaultValue,
            Margin = new Thickness(10),
            MinWidth = 300
        };
        textBox.GotFocus += (_, _) => textBox.SelectAll();
        var label = new TextBlock
        {
            Text = message,
            Margin = new Thickness(10, 10, 10, 0)
        };
        var okButton = new Button
        {
            Content = "OK",
            IsDefault = true,
            Width = 80,
            Height = 30,
            Margin = new Thickness(10)
        };
        var cancelButton = new Button
        {
            Content = "キャンセル",
            IsCancel = true,
            Width = 80,
            Height = 30,
            Margin = new Thickness(5)
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10)
        };
        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        var panel = new StackPanel();
        panel.Children.Add(label);
        panel.Children.Add(textBox);
        panel.Children.Add(buttonPanel);

        var window = new Window
        {
            Title = title,
            Content = panel,
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current.MainWindow
        };

        textBox.Focus();
        okButton.Click += (_, _) => { window.DialogResult = true; };

        return window.ShowDialog() == true ? textBox.Text : null;
    }
}
