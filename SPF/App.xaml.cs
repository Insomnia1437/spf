using System.Windows;
using ModernWpf;

namespace SPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Forces the ModernWpf framework to load Light Theme controls
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
        }
    }
}
