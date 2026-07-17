using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SPF.Models;
using SPF.Services;

namespace SPF
{
    /// <summary>
    /// Interaction logic for TunnelEditWindow.xaml
    /// </summary>
    public partial class TunnelEditWindow : Window
    {
        private readonly List<SshHostInfo> _sshConfigs = new();
        private bool _isLoading = true;
        public TunnelConfig Config { get; private set; }

        public TunnelEditWindow(TunnelConfig? config = null)
        {
            InitializeComponent();

            // Assign config or create a new one
            Config = config ?? new TunnelConfig();

            _isLoading = true;

            // 1. Populate Auth Methods
            if (AuthMethodCombo != null)
            {
                AuthMethodCombo.ItemsSource = Enum.GetValues(typeof(SshAuthMethod));
            }

            // 2. Populate Local IP adapters
            var adapters = TunnelService.GetLocalAdapters();
            if (LocalIpCombo != null)
            {
                LocalIpCombo.ItemsSource = adapters;
            }

            // 3. Load SSH config file rules for autocompletion
            _sshConfigs = SshConfigParser.ParseSshConfig();
            if (SshHostCombo != null)
            {
                SshHostCombo.ItemsSource = _sshConfigs.Select(c => c.Alias).ToList();
            }

            // 4. Fill form with values
            LoadConfigIntoForm();
            UpdateUiState();

            _isLoading = false;
        }

        private void LoadConfigIntoForm()
        {
            if (NameInput == null) return;

            NameInput.Text = Config.Name;

            if (AutoReconnectCheck != null)
                AutoReconnectCheck.IsChecked = Config.AutoReconnect;

            if (LocalIpCombo != null)
                LocalIpCombo.Text = Config.LocalBindAddress;

            if (LocalPortInput != null)
                LocalPortInput.Text = Config.LocalPort > 0 ? Config.LocalPort.ToString() : "";

            if (RemoteHostInput != null)
                RemoteHostInput.Text = Config.RemoteHost;

            if (RemotePortInput != null)
                RemotePortInput.Text = Config.RemotePort > 0 ? Config.RemotePort.ToString() : "";

            if (SshHostCombo != null)
                SshHostCombo.Text = Config.SshHost;

            if (SshPortInput != null)
                SshPortInput.Text = Config.SshPort.ToString();

            if (SshUserInput != null)
                SshUserInput.Text = Config.SshUser;

            if (AuthMethodCombo != null)
                AuthMethodCombo.SelectedItem = Config.AuthMethod;

            if (Config.AuthMethod == SshAuthMethod.PrivateKey)
            {
                if (SshKeyPathInput != null)
                    SshKeyPathInput.Text = Config.SshKeyPath;
                if (KeyPassphraseInput != null)
                    KeyPassphraseInput.Password = Config.SshKeyPassphrase;
            }

            // Radio Button Type
            if (LocalRadio != null && RemoteRadio != null && DynamicRadio != null)
            {
                switch (Config.Type)
                {
                    case TunnelType.Local:
                        LocalRadio.IsChecked = true;
                        break;
                    case TunnelType.Remote:
                        RemoteRadio.IsChecked = true;
                        break;
                    case TunnelType.Dynamic:
                        DynamicRadio.IsChecked = true;
                        break;
                }
            }
        }

        private void ClearInputs()
        {
            if (LocalPortInput != null) LocalPortInput.Clear();
            if (LocalIpCombo != null) LocalIpCombo.Text = "127.0.0.1";
            if (RemoteHostInput != null) RemoteHostInput.Clear();
            if (RemotePortInput != null) RemotePortInput.Clear();
            if (SshHostCombo != null) SshHostCombo.Text = "";
            if (SshPortInput != null) SshPortInput.Text = "22";
            if (SshUserInput != null) SshUserInput.Clear();
            if (SshKeyPathInput != null) SshKeyPathInput.Clear();
            if (KeyPassphraseInput != null) KeyPassphraseInput.Clear();
        }

        private void UpdateUiState()
        {
            // Null safety checks during early initialization
            if (LocalRadio == null || RemoteRadio == null || DynamicRadio == null ||
                LocalCardTitle == null || RemoteCardTitle == null ||
                ArrowLocalToSsh == null || ArrowSshToRemote == null ||
                ArrowSshToRemoteGrid == null || RemoteCardColumn == null ||
                RemoteCardBorder == null || AuthMethodCombo == null)
            {
                return;
            }

            var selectedType = GetSelectedType();

            // 1. Update visual flow direction and titles based on tunnel type
            switch (selectedType)
            {
                case TunnelType.Local:
                    LocalCardTitle.Text = "Local Listener (PC)";
                    RemoteCardTitle.Text = "Remote Destination";

                    ArrowLocalToSsh.Text = "➔";
                    ArrowSshToRemote.Text = "➔";

                    ArrowSshToRemoteGrid.Visibility = Visibility.Visible;
                    RemoteCardBorder.Visibility = Visibility.Visible;
                    RemoteCardColumn.Width = new GridLength(1, GridUnitType.Star);
                    break;

                case TunnelType.Remote:
                    LocalCardTitle.Text = "Local Destination";
                    RemoteCardTitle.Text = "Remote Listener (Server)";

                    ArrowLocalToSsh.Text = "←";
                    ArrowSshToRemote.Text = "←";

                    ArrowSshToRemoteGrid.Visibility = Visibility.Visible;
                    RemoteCardBorder.Visibility = Visibility.Visible;
                    RemoteCardColumn.Width = new GridLength(1, GridUnitType.Star);
                    break;

                case TunnelType.Dynamic:
                    LocalCardTitle.Text = "SOCKS Proxy Listener";

                    ArrowLocalToSsh.Text = "➔";

                    ArrowSshToRemoteGrid.Visibility = Visibility.Collapsed;
                    RemoteCardBorder.Visibility = Visibility.Collapsed;
                    RemoteCardColumn.Width = new GridLength(0); // Hide remote column
                    break;
            }

            // 2. Toggle Key Auth controls inside the SSH card
            var auth = (SshAuthMethod?)AuthMethodCombo.SelectedItem ?? SshAuthMethod.Password;
            if (KeyAuthPanel != null)
            {
                KeyAuthPanel.Visibility = (auth == SshAuthMethod.PrivateKey) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private TunnelType GetSelectedType()
        {
            if (LocalRadio?.IsChecked == true) return TunnelType.Local;
            if (RemoteRadio?.IsChecked == true) return TunnelType.Remote;
            return TunnelType.Dynamic;
        }

        private void TunnelType_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            ClearInputs();
            UpdateUiState();
        }

        private void AuthMethodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateUiState();
        }

        private void SshHostCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SshHostCombo == null || SshPortInput == null || SshUserInput == null || AuthMethodCombo == null || SshKeyPathInput == null) return;

            string selectedAlias = SshHostCombo.SelectedItem as string ?? string.Empty;
            if (string.IsNullOrEmpty(selectedAlias)) return;

            var match = _sshConfigs.FirstOrDefault(c => c.Alias.Equals(selectedAlias, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                SshPortInput.Text = match.Port.ToString();
                SshUserInput.Text = match.User;
                if (!string.IsNullOrEmpty(match.IdentityFile))
                {
                    AuthMethodCombo.SelectedItem = SshAuthMethod.PrivateKey;
                    SshKeyPathInput.Text = match.IdentityFile;
                }
                UpdateUiState();
            }
        }

        private void BrowseKey_Click(object sender, RoutedEventArgs e)
        {
            if (SshKeyPathInput == null) return;

            var dialog = new OpenFileDialog();
            string sshFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
            if (Directory.Exists(sshFolder))
            {
                dialog.InitialDirectory = sshFolder;
            }
            dialog.Filter = "Private Key Files|*.*|All Files (*.*)|*.*";

            if (dialog.ShowDialog() == true)
            {
                SshKeyPathInput.Text = dialog.FileName;
            }
        }

        private void PortInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, "[0-9]");
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (NameInput == null || LocalPortInput == null || LocalIpCombo == null ||
                SshHostCombo == null || SshPortInput == null || SshUserInput == null ||
                AuthMethodCombo == null)
            {
                return;
            }

            // Validate inputs
            if (string.IsNullOrWhiteSpace(NameInput.Text))
            {
                MessageBox.Show("Please enter a Friendly Name for the tunnel.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(LocalPortInput.Text, out int localPort) || localPort <= 0 || localPort > 65535)
            {
                MessageBox.Show("Please enter a valid Local Port (1-65535).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var type = GetSelectedType();
            if (type != TunnelType.Dynamic)
            {
                if (RemoteHostInput == null || RemotePortInput == null || string.IsNullOrWhiteSpace(RemoteHostInput.Text))
                {
                    MessageBox.Show("Please enter a Destination Host.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!int.TryParse(RemotePortInput.Text, out int remotePort) || remotePort <= 0 || remotePort > 65535)
                {
                    MessageBox.Show("Please enter a valid Destination Port (1-65535).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Config.RemoteHost = RemoteHostInput.Text.Trim();
                Config.RemotePort = remotePort;
            }

            if (string.IsNullOrWhiteSpace(SshHostCombo.Text))
            {
                MessageBox.Show("Please enter the SSH Host Server address.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(SshPortInput.Text, out int sshPort) || sshPort <= 0 || sshPort > 65535)
            {
                MessageBox.Show("Please enter a valid SSH Port (1-65535).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(SshUserInput.Text))
            {
                MessageBox.Show("Please enter the SSH Username.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var auth = (SshAuthMethod)AuthMethodCombo.SelectedItem;
            if (auth == SshAuthMethod.PrivateKey && SshKeyPathInput != null && string.IsNullOrWhiteSpace(SshKeyPathInput.Text))
            {
                MessageBox.Show("Please select a Private Key file path for key-based authentication.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Assign settings
            Config.Name = NameInput.Text.Trim();
            Config.Type = type;
            Config.LocalBindAddress = LocalIpCombo.Text.Trim();
            Config.LocalPort = localPort;
            Config.SshHost = SshHostCombo.Text.Trim();
            Config.SshPort = sshPort;
            Config.SshUser = SshUserInput.Text.Trim();
            Config.AuthMethod = auth;

            if (AutoReconnectCheck != null)
            {
                Config.AutoReconnect = AutoReconnectCheck.IsChecked ?? true;
            }

            if (auth == SshAuthMethod.PrivateKey)
            {
                if (SshKeyPathInput != null)
                {
                    Config.SshKeyPath = SshKeyPathInput.Text.Trim();
                }
                if (KeyPassphraseInput != null)
                {
                    Config.SshKeyPassphrase = KeyPassphraseInput.Password;
                }
            }
            else
            {
                Config.SshKeyPath = string.Empty;
                Config.SshKeyPassphrase = string.Empty;
            }

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
