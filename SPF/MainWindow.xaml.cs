using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SPF.Models;
using SPF.Services;

namespace SPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly TunnelService _tunnelService;
        private readonly ObservableCollection<TunnelRow> _tunnelsList = new();

        public MainWindow()
        {
            InitializeComponent();

            // 1. Initialize core services
            _tunnelService = new TunnelService();
            _tunnelService.StatusChanged += TunnelService_StatusChanged;
            _tunnelService.LogReceived += TunnelService_LogReceived;

            // 2. Bind Grid to ObservableCollection
            TunnelsGrid.ItemsSource = _tunnelsList;

            // 3. Load configurations
            LoadConfiguration();

            // Log startup
            AppendLog("System", "SimplePortForwarder (SPF) initialized.");
            AppendLog("System", $"Active configuration file: {ConfigManager.GetActiveConfigPath()}");
        }

        private void LoadConfiguration()
        {
            _tunnelsList.Clear();
            var tunnels = ConfigManager.LoadTunnels();
            foreach (var t in tunnels)
            {
                _tunnelsList.Add(new TunnelRow { Config = t, Status = TunnelStatus.Stopped });
            }

            UpdateSummary();
        }

        private void SaveConfiguration()
        {
            try
            {
                var configs = _tunnelsList.Select(r => r.Config).ToList();
                ConfigManager.SaveTunnels(configs);
            }
            catch (Exception ex)
            {
                AppendLog("System", $"Error saving configuration file: {ex.Message}", isError: true);
                MessageBox.Show($"Failed to save configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateSummary()
        {
            int total = _tunnelsList.Count;
            int active = _tunnelsList.Count(t => t.IsRunningOrConnecting);
            TunnelsSummaryLabel.Text = $"Tunnels: {total} total | {active} active";
        }

        // --- TunnelService Event Handlers ---

        private void TunnelService_StatusChanged(object? sender, TunnelStatusEventArgs e)
        {
            // Marshal back to the UI thread
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var match = _tunnelsList.FirstOrDefault(t => t.Id == e.TunnelId);
                if (match != null)
                {
                    match.Status = e.Status;
                    UpdateSummary();
                }
            }));
        }

        private void TunnelService_LogReceived(object? sender, TunnelLogEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                AppendLog(e.TunnelName, e.Message, e.IsError);
            }));
        }

        private void AppendLog(string source, string message, bool isError = false)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string errorTag = isError ? "[ERROR] " : "";
            string logLine = $"[{timestamp}] [{source}] {errorTag}{message}{Environment.NewLine}";

            LogTextBox.AppendText(logLine);
            LogScrollViewer.ScrollToEnd();
        }

        // --- Toolbar Actions ---

        private void NewTunnel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var editWindow = new TunnelEditWindow
                {
                    Owner = this
                };

                if (editWindow.ShowDialog() == true)
                {
                    var newRow = new TunnelRow
                    {
                        Config = editWindow.Config,
                        Status = TunnelStatus.Stopped
                    };
                    _tunnelsList.Add(newRow);
                    SaveConfiguration();
                    UpdateSummary();
                    AppendLog("System", $"Added new tunnel: {newRow.Name}");
                }
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException != null ? $"\n\nInner: {ex.InnerException.Message}" : "";
                MessageBox.Show($"Error opening Tunnel Edit window:\n\n{ex.Message}{inner}\n\nStack Trace:\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                AppendLog("System", $"Error opening TunnelEditWindow: {ex.Message}{inner}\n{ex.StackTrace}", isError: true);
            }
        }

        private void StartAll_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("System", "Starting all tunnels...");
            foreach (var row in _tunnelsList)
            {
                if (!row.IsRunningOrConnecting)
                {
                    string? password = null;
                    if (row.Config.AuthMethod == SshAuthMethod.Password)
                    {
                        var prompt = new PasswordPromptWindow(row.Config.SshUser, row.Config.SshHost, row.Config.SshPort)
                        {
                            Owner = this
                        };
                        if (prompt.ShowDialog() == true)
                        {
                            password = prompt.Password;
                        }
                        else
                        {
                            AppendLog("System", $"Start skipped for tunnel '{row.Name}': password prompt cancelled.");
                            continue;
                        }
                    }
                    _tunnelService.StartTunnel(row.Config, password);
                }
            }
        }

        private void StopAll_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("System", "Stopping all active tunnels...");
            foreach (var row in _tunnelsList)
            {
                if (row.IsRunningOrConnecting)
                {
                    _tunnelService.StopTunnel(row.Id, row.Name);
                }
            }
        }

        private void ExportConfig_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                FileName = $"{DateTime.Now:yyyyMMdd}_spf_backup.json",
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var configs = _tunnelsList.Select(r => r.Config).ToList();
                    ConfigManager.ExportConfig(dialog.FileName, configs);
                    AppendLog("System", $"Configurations exported to {dialog.FileName}");
                    MessageBox.Show("Configuration exported successfully.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    AppendLog("System", $"Export failed: {ex.Message}", isError: true);
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportConfig_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var imported = ConfigManager.ImportConfig(dialog.FileName);

                    var result = MessageBox.Show(
                        "Would you like to overwrite your current configuration?\n\n- Click YES to overwrite.\n- Click NO to append imported tunnels to your existing list.",
                        "Import Tunnels",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question
                    );

                    if (result == MessageBoxResult.Cancel)
                    {
                        return;
                    }

                    if (result == MessageBoxResult.Yes)
                    {
                        // Stop all active tunnels first
                        StopAll_Click(this, new RoutedEventArgs());
                        _tunnelsList.Clear();
                    }

                    foreach (var config in imported)
                    {
                        // Generate a new ID if appending to prevent duplicates
                        if (result == MessageBoxResult.No)
                        {
                            config.Id = Guid.NewGuid().ToString();
                        }

                        _tunnelsList.Add(new TunnelRow { Config = config, Status = TunnelStatus.Stopped });
                    }

                    SaveConfiguration();
                    UpdateSummary();
                    AppendLog("System", $"Successfully imported {imported.Count} configurations from {dialog.FileName}");
                    MessageBox.Show($"Imported {imported.Count} tunnels successfully.", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    AppendLog("System", $"Import failed: {ex.Message}", isError: true);
                    MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenConfigFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? folder = Path.GetDirectoryName(ConfigManager.GetActiveConfigPath());
                if (folder != null && Directory.Exists(folder))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = folder,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                AppendLog("System", $"Failed to open config folder: {ex.Message}", isError: true);
            }
        }

        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
        }

        // --- DataGrid Context Actions ---

        private void ToggleTunnel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TunnelRow row)
            {
                if (row.IsRunningOrConnecting)
                {
                    _tunnelService.StopTunnel(row.Id, row.Name);
                }
                else
                {
                    string? password = null;
                    if (row.Config.AuthMethod == SshAuthMethod.Password)
                    {
                        var prompt = new PasswordPromptWindow(row.Config.SshUser, row.Config.SshHost, row.Config.SshPort)
                        {
                            Owner = this
                        };
                        if (prompt.ShowDialog() == true)
                        {
                            password = prompt.Password;
                        }
                        else
                        {
                            return; // Cancelled
                        }
                    }
                    _tunnelService.StartTunnel(row.Config, password);
                }
            }
        }

        private void EditTunnel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TunnelRow row && row.IsEditable)
            {
                try
                {
                    // Create a deep copy of config to edit, avoiding modifying active grid until saved
                    var json = System.Text.Json.JsonSerializer.Serialize(row.Config);
                    var configCopy = System.Text.Json.JsonSerializer.Deserialize<TunnelConfig>(json);

                    if (configCopy == null) return;

                    var editWindow = new TunnelEditWindow(configCopy)
                    {
                        Owner = this
                    };

                    if (editWindow.ShowDialog() == true)
                    {
                        row.Config = editWindow.Config;
                        row.RefreshAllProperties();
                        SaveConfiguration();
                        AppendLog("System", $"Updated config: {row.Name}");
                    }
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException != null ? $"\n\nInner: {ex.InnerException.Message}" : "";
                    MessageBox.Show($"Error opening Tunnel Edit window:\n\n{ex.Message}{inner}\n\nStack Trace:\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    AppendLog("System", $"Error opening TunnelEditWindow: {ex.Message}{inner}\n{ex.StackTrace}", isError: true);
                }
            }
        }

        private void CopyTunnel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TunnelRow row && row.IsEditable)
            {
                try
                {
                    // Create a deep copy of config to copy
                    var json = System.Text.Json.JsonSerializer.Serialize(row.Config);
                    var configCopy = System.Text.Json.JsonSerializer.Deserialize<TunnelConfig>(json);

                    if (configCopy == null) return;

                    // Assign new ID and suffix name
                    configCopy.Id = Guid.NewGuid().ToString();
                    configCopy.Name = $"{configCopy.Name}_copy";

                    var newRow = new TunnelRow
                    {
                        Config = configCopy,
                        Status = TunnelStatus.Stopped
                    };

                    _tunnelsList.Add(newRow);
                    SaveConfiguration();
                    UpdateSummary();
                    AppendLog("System", $"Copied tunnel '{row.Name}' to '{newRow.Name}'");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error copying tunnel:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DeleteTunnel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TunnelRow row && row.IsEditable)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete tunnel '{row.Name}'?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result == MessageBoxResult.Yes)
                {
                    _tunnelsList.Remove(row);
                    SaveConfiguration();
                    UpdateSummary();
                    AppendLog("System", $"Deleted tunnel: {row.Name}");
                }
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            // Clean up and stop all active SSH listeners/connections on close
            _tunnelService.Dispose();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            MessageBox.Show(
                "Simple Port Forwarder (SPF)\n" +
                "Author: DW\n" +
                "Source: https://github.com/Insomnia1437/spf\n" +
                $"Version: {version}\n\n" +
                "A lightweight, portable Windows utility to manage and run SSH tunnels (Local, Remote, and Dynamic).\n" +
                "Configurations are stored locally in tunnels.json.\n\n" +
                "Designed for secure, reliable tunneling.",
                "About SPF",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
    }

    /// <summary>
    /// Wrapper view model for displaying TunnelConfig entries inside the DataGrid with active status binding.
    /// </summary>
    public class TunnelRow : INotifyPropertyChanged
    {
        private TunnelStatus _status = TunnelStatus.Stopped;

        public TunnelConfig Config { get; set; } = new();

        public string Id => Config.Id;
        public string Name => Config.Name;
        public string Type => Config.Type switch
        {
            TunnelType.Local => "Local",
            TunnelType.Remote => "Remote",
            _ => "Dynamic"
        };

        public string LocalDisplay => $"{Config.LocalBindAddress}:{Config.LocalPort}";
        public string SshDisplay => $"{Config.SshUser}@{Config.SshHost}:{Config.SshPort}";

        public string DestinationDisplay => Config.Type == TunnelType.Dynamic
            ? "(SOCKS5 Proxy)"
            : $"{Config.RemoteHost}:{Config.RemotePort}";

        public TunnelStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(StatusDisplay));
                    OnPropertyChanged(nameof(StatusBrush));
                    OnPropertyChanged(nameof(StatusBgBrush));
                    OnPropertyChanged(nameof(StatusTextBrush));
                    OnPropertyChanged(nameof(IsRunningOrConnecting));
                    OnPropertyChanged(nameof(IsEditable));
                    OnPropertyChanged(nameof(StartButtonText));
                    OnPropertyChanged(nameof(StartButtonSymbol));
                    OnPropertyChanged(nameof(ActionButtonBg));
                    OnPropertyChanged(nameof(ActionButtonFg));
                    OnPropertyChanged(nameof(IsStartEnabled));
                    OnPropertyChanged(nameof(IsStopEnabled));
                }
            }
        }

        public string StatusDisplay => _status switch
        {
            TunnelStatus.Stopped => "Stopped",
            TunnelStatus.Connecting => "Connecting",
            TunnelStatus.Running => "Active",
            TunnelStatus.Reconnecting => "Reconnecting",
            TunnelStatus.Error => "Error",
            TunnelStatus.Conflict => "Port Busy",
            _ => "Stopped"
        };

        // Core dot color
        public Brush StatusBrush => _status switch
        {
            TunnelStatus.Running => new SolidColorBrush(Color.FromRgb(52, 168, 83)), // green
            TunnelStatus.Connecting => new SolidColorBrush(Color.FromRgb(251, 188, 5)), // yellow
            TunnelStatus.Reconnecting => new SolidColorBrush(Color.FromRgb(244, 180, 0)), // orange
            TunnelStatus.Error => new SolidColorBrush(Color.FromRgb(219, 68, 85)), // red
            TunnelStatus.Conflict => new SolidColorBrush(Color.FromRgb(234, 67, 53)), // crimson
            _ => Brushes.DarkGray
        };

        // Soft pastel pill background
        public Brush StatusBgBrush => _status switch
        {
            TunnelStatus.Running => new SolidColorBrush(Color.FromArgb(30, 52, 168, 83)), // very light green
            TunnelStatus.Connecting => new SolidColorBrush(Color.FromArgb(30, 251, 188, 5)), // very light yellow
            TunnelStatus.Reconnecting => new SolidColorBrush(Color.FromArgb(30, 244, 180, 0)), // very light orange
            TunnelStatus.Error => new SolidColorBrush(Color.FromArgb(30, 219, 68, 85)), // very light red
            TunnelStatus.Conflict => new SolidColorBrush(Color.FromArgb(30, 234, 67, 53)), // very light crimson
            _ => new SolidColorBrush(Color.FromArgb(20, 128, 128, 128))
        };

        // Readable dark color for text in the badge
        public Brush StatusTextBrush => _status switch
        {
            TunnelStatus.Running => new SolidColorBrush(Color.FromRgb(19, 115, 51)), // dark green
            TunnelStatus.Connecting => new SolidColorBrush(Color.FromRgb(180, 120, 0)), // dark yellow
            TunnelStatus.Reconnecting => new SolidColorBrush(Color.FromRgb(190, 110, 0)), // dark orange
            TunnelStatus.Error => new SolidColorBrush(Color.FromRgb(197, 34, 31)), // dark red
            TunnelStatus.Conflict => new SolidColorBrush(Color.FromRgb(197, 34, 31)), // dark red
            _ => Brushes.DimGray
        };

        public bool IsRunningOrConnecting => _status == TunnelStatus.Running || _status == TunnelStatus.Connecting || _status == TunnelStatus.Reconnecting;
        public bool IsEditable => !IsRunningOrConnecting;

        public string StartButtonText => IsRunningOrConnecting ? "Stop" : "Start";
        public string StartButtonSymbol => IsRunningOrConnecting ? "■" : "▶";

        public Brush ActionButtonBg => IsRunningOrConnecting
            ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) // solid red
            : new SolidColorBrush(Color.FromRgb(16, 185, 129)); // solid green

        public Brush ActionButtonFg => Brushes.White;

        public bool IsStartEnabled => !IsRunningOrConnecting;
        public bool IsStopEnabled => IsRunningOrConnecting;

        public void RefreshAllProperties()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Type));
            OnPropertyChanged(nameof(LocalDisplay));
            OnPropertyChanged(nameof(SshDisplay));
            OnPropertyChanged(nameof(DestinationDisplay));
            OnPropertyChanged(nameof(StatusDisplay));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(StatusBgBrush));
            OnPropertyChanged(nameof(StatusTextBrush));
            OnPropertyChanged(nameof(IsRunningOrConnecting));
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(StartButtonText));
            OnPropertyChanged(nameof(StartButtonSymbol));
            OnPropertyChanged(nameof(ActionButtonBg));
            OnPropertyChanged(nameof(ActionButtonFg));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
