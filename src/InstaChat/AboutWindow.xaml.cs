using System.Reflection;
using System.Windows;

namespace InstaChat;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
