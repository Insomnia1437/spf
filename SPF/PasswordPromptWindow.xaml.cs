using System.Windows;

namespace SPF
{
    /// <summary>
    /// Interaction logic for PasswordPromptWindow.xaml
    /// </summary>
    public partial class PasswordPromptWindow : Window
    {
        public string Password { get; private set; } = string.Empty;

        public PasswordPromptWindow(string sshUser, string sshHost, int sshPort)
        {
            InitializeComponent();
            
            ServerDetailsLabel.Text = $"Connecting to {sshUser}@{sshHost}:{sshPort}";
            
            Loaded += (s, e) =>
            {
                PasswordInput.Focus();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Password = PasswordInput.Password;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
