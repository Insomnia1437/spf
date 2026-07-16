using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;
using SPF.Models;

namespace SPF.Services
{
    /// <summary>
    /// Represents the current execution status of a tunnel.
    /// </summary>
    public enum TunnelStatus
    {
        Stopped,
        Connecting,
        Running,
        Reconnecting,
        Error,
        Conflict
    }

    /// <summary>
    /// Arguments for tunnel status change events.
    /// </summary>
    public class TunnelStatusEventArgs : EventArgs
    {
        public string TunnelId { get; }
        public TunnelStatus Status { get; }
        public string Message { get; }

        public TunnelStatusEventArgs(string tunnelId, TunnelStatus status, string message = "")
        {
            TunnelId = tunnelId;
            Status = status;
            Message = message;
        }
    }

    /// <summary>
    /// Arguments for log message events.
    /// </summary>
    public class TunnelLogEventArgs : EventArgs
    {
        public string TunnelId { get; }
        public string TunnelName { get; }
        public string Message { get; }
        public bool IsError { get; }

        public TunnelLogEventArgs(string tunnelId, string tunnelName, string message, bool isError = false)
        {
            TunnelId = tunnelId;
            TunnelName = tunnelName;
            Message = message;
            IsError = isError;
        }
    }

    /// <summary>
    /// Service responsible for managing SSH connections, port checks, and automatic reconnections.
    /// </summary>
    public class TunnelService : IDisposable
    {
        // Event triggered when a tunnel's status changes
        public event EventHandler<TunnelStatusEventArgs>? StatusChanged;

        // Event triggered when a new log message is recorded
        public event EventHandler<TunnelLogEventArgs>? LogReceived;

        private readonly ConcurrentDictionary<string, SshClient> _sshClients = new();
        private readonly ConcurrentDictionary<string, List<ForwardedPort>> _forwardedPorts = new();
        private readonly ConcurrentDictionary<string, TunnelStatus> _statuses = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _reconnectTokens = new();
        private readonly ConcurrentDictionary<string, string> _sessionPasswords = new();
        private readonly object _lock = new();

        /// <summary>
        /// Retrieves the current status of a specific tunnel.
        /// </summary>
        public TunnelStatus GetStatus(string tunnelId)
        {
            return _statuses.TryGetValue(tunnelId, out var status) ? status : TunnelStatus.Stopped;
        }

        /// <summary>
        /// Starts a tunnel asynchronously in the background.
        /// </summary>
        public void StartTunnel(TunnelConfig config, string? password = null)
        {
            lock (_lock)
            {
                // If already running or connecting, ignore
                var status = GetStatus(config.Id);
                if (status == TunnelStatus.Running || status == TunnelStatus.Connecting || status == TunnelStatus.Reconnecting)
                {
                    return;
                }

                if (!string.IsNullOrEmpty(password))
                {
                    _sessionPasswords[config.Id] = password;
                }

                // Cancel any pending reconnect tasks
                CancelReconnectTask(config.Id);

                // Initialize cancellation source for connection
                var cts = new CancellationTokenSource();
                _reconnectTokens[config.Id] = cts;

                SetStatus(config.Id, TunnelStatus.Connecting, "Initializing connection...");
                Log(config.Id, config.Name, $"Starting tunnel config '{config.Name}' ({config.Type} Forwarding)...");

                Task.Run(() => ConnectAndSetupTunnelAsync(config, cts.Token));
            }
        }

        /// <summary>
        /// Stops a tunnel and releases all associated sockets and SSH connections.
        /// </summary>
        public void StopTunnel(string tunnelId, string tunnelName)
        {
            lock (_lock)
            {
                Log(tunnelId, tunnelName, "Stopping tunnel...");
                CancelReconnectTask(tunnelId);
                CleanupTunnelResources(tunnelId);
                SetStatus(tunnelId, TunnelStatus.Stopped, "Stopped by user.");
                Log(tunnelId, tunnelName, "Tunnel stopped.");
            }
        }

        /// <summary>
        /// Scans active TCP listeners in Windows to check if a port is already bound on a specific IP.
        /// </summary>
        public static bool IsPortOccupied(string ipAddress, int port, out string details)
        {
            details = string.Empty;
            try
            {
                var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
                var tcpListeners = ipProperties.GetActiveTcpListeners();
                
                if (!IPAddress.TryParse(ipAddress, out var targetIp))
                {
                    details = $"Invalid IP address format: {ipAddress}";
                    return true;
                }

                foreach (var listener in tcpListeners)
                {
                    if (listener.Port == port)
                    {
                        // Match all adapters (0.0.0.0 / [::]) or matching specific IP
                        if (targetIp.Equals(IPAddress.Any) || 
                            listener.Address.Equals(IPAddress.Any) ||
                            targetIp.Equals(listener.Address) ||
                            (targetIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && targetIp.Equals(IPAddress.IPv6Any)) ||
                            listener.Address.Equals(IPAddress.IPv6Any))
                        {
                            details = $"Port {port} is already bound on {listener.Address}";
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                details = $"Failed to query TCP ports: {ex.Message}";
                return true;
            }
            return false;
        }

        /// <summary>
        /// Scans all local network interfaces and returns their descriptive names and IP addresses.
        /// </summary>
        public static List<string> GetLocalAdapters()
        {
            var adaptersList = new List<string> { "127.0.0.1", "0.0.0.0" };
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }

                    var ipProperties = ni.GetIPProperties();
                    foreach (var ip in ipProperties.UnicastAddresses)
                    {
                        // We support IPv4 addresses for simplicity
                        if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            string ipStr = ip.Address.ToString();
                            if (ipStr != "127.0.0.1" && !adaptersList.Contains(ipStr))
                            {
                                adaptersList.Add(ipStr);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to retrieve adapters: {ex.Message}");
            }
            return adaptersList;
        }

        private async Task ConnectAndSetupTunnelAsync(TunnelConfig config, CancellationToken token)
        {
            string tunnelId = config.Id;
            string tunnelName = config.Name;

            // 1. Port conflict check (only for local-listening types: Local and Dynamic)
            if (config.Type == TunnelType.Local || config.Type == TunnelType.Dynamic)
            {
                if (IsPortOccupied(config.LocalBindAddress, config.LocalPort, out string conflictDetails))
                {
                    string errMsg = $"Port Conflict: {conflictDetails}";
                    Log(tunnelId, tunnelName, errMsg, isError: true);
                    SetStatus(tunnelId, TunnelStatus.Conflict, errMsg);
                    return;
                }
            }

            // 2. Establish SSH connection
            SshClient? client = null;
            try
            {
                if (token.IsCancellationRequested) return;

                ConnectionInfo connectionInfo = CreateConnectionInfo(config);
                client = new SshClient(connectionInfo);
                
                // Set timeouts to prevent infinite blocking
                client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);
                client.KeepAliveInterval = TimeSpan.FromSeconds(30);

                // Register Error handler to catch connection drops
                client.ErrorOccurred += (sender, e) =>
                {
                    Log(tunnelId, tunnelName, $"SSH Client Connection Error: {e.Exception.Message}", isError: true);
                    HandleDisconnect(config);
                };

                Log(tunnelId, tunnelName, $"Connecting to SSH server {config.SshHost}:{config.SshPort}...");
                
                // Run connection in Task to avoid blocking
                await Task.Run(() => client.Connect(), token);

                if (!client.IsConnected)
                {
                    throw new SshException("Connection failed: Client not connected.");
                }

                Log(tunnelId, tunnelName, "SSH connection established successfully.");

                // 3. Configure Port Forwarding tunnels
                var activePorts = new List<ForwardedPort>();

                if (token.IsCancellationRequested)
                {
                    client.Disconnect();
                    client.Dispose();
                    return;
                }

                if (config.Type == TunnelType.Local)
                {
                    var portLocal = new ForwardedPortLocal(config.LocalBindAddress, (uint)config.LocalPort, config.RemoteHost, (uint)config.RemotePort);
                    client.AddForwardedPort(portLocal);
                    
                    portLocal.Exception += (s, e) => Log(tunnelId, tunnelName, $"Local Forwarding Port Error: {e.Exception.Message}", isError: true);
                    
                    Log(tunnelId, tunnelName, $"Starting local port forwarding: {config.LocalBindAddress}:{config.LocalPort} -> {config.RemoteHost}:{config.RemotePort}");
                    portLocal.Start();
                    activePorts.Add(portLocal);
                }
                else if (config.Type == TunnelType.Remote)
                {
                    // For Remote forwarding, bindHost is on the SSH server (usually 127.0.0.1 or 0.0.0.0 to listen on all interfaces)
                    // The traffic at RemotePort on the server will be forwarded to LocalBindAddress:LocalPort on the client side
                    var portRemote = new ForwardedPortRemote("127.0.0.1", (uint)config.RemotePort, config.LocalBindAddress, (uint)config.LocalPort);
                    client.AddForwardedPort(portRemote);

                    portRemote.Exception += (s, e) => Log(tunnelId, tunnelName, $"Remote Forwarding Port Error: {e.Exception.Message}", isError: true);

                    Log(tunnelId, tunnelName, $"Starting remote port forwarding: [SSH Server]:{config.RemotePort} -> {config.LocalBindAddress}:{config.LocalPort}");
                    
                    // Run on thread pool because RemotePort.Start can block or throw if server GatewayPorts configuration denies it
                    await Task.Run(() => portRemote.Start(), token);
                    activePorts.Add(portRemote);
                }
                else if (config.Type == TunnelType.Dynamic)
                {
                    var portDynamic = new ForwardedPortDynamic(config.LocalBindAddress, (uint)config.LocalPort);
                    client.AddForwardedPort(portDynamic);

                    portDynamic.Exception += (s, e) => Log(tunnelId, tunnelName, $"SOCKS Sockets Error: {e.Exception.Message}", isError: true);

                    Log(tunnelId, tunnelName, $"Starting SOCKS5 proxy server on {config.LocalBindAddress}:{config.LocalPort}");
                    portDynamic.Start();
                    activePorts.Add(portDynamic);
                }

                // Register client resources
                _sshClients[tunnelId] = client;
                _forwardedPorts[tunnelId] = activePorts;

                SetStatus(tunnelId, TunnelStatus.Running, "Tunnel is active.");
                Log(tunnelId, tunnelName, "Tunnel started successfully.");
            }
            catch (Exception ex)
            {
                if (client != null)
                {
                    try { client.Disconnect(); } catch { /* Ignore */ }
                    client.Dispose();
                }

                string errMessage = $"SSH connection error: {ex.Message}";
                Log(tunnelId, tunnelName, errMessage, isError: true);
                SetStatus(tunnelId, TunnelStatus.Error, errMessage);

                if (config.AutoReconnect && !token.IsCancellationRequested)
                {
                    // Trigger reconnect task
                    HandleDisconnect(config);
                }
            }
        }

        private ConnectionInfo CreateConnectionInfo(TunnelConfig config)
        {
            var authMethods = new List<AuthenticationMethod>();

            if (config.AuthMethod == SshAuthMethod.Password)
            {
                _sessionPasswords.TryGetValue(config.Id, out var password);
                authMethods.Add(new PasswordAuthenticationMethod(config.SshUser, password ?? string.Empty));
            }
            else // PrivateKey
            {
                if (!File.Exists(config.SshKeyPath))
                {
                    throw new FileNotFoundException($"Private key file not found: {config.SshKeyPath}");
                }

                PrivateKeyFile keyFile;
                if (string.IsNullOrEmpty(config.SshKeyPassphrase))
                {
                    keyFile = new PrivateKeyFile(config.SshKeyPath);
                }
                else
                {
                    keyFile = new PrivateKeyFile(config.SshKeyPath, config.SshKeyPassphrase);
                }

                authMethods.Add(new PrivateKeyAuthenticationMethod(config.SshUser, keyFile));
            }

            return new ConnectionInfo(config.SshHost, config.SshPort, config.SshUser, authMethods.ToArray());
        }

        private void HandleDisconnect(TunnelConfig config)
        {
            lock (_lock)
            {
                var status = GetStatus(config.Id);
                if (status == TunnelStatus.Stopped || status == TunnelStatus.Reconnecting)
                {
                    return; // Already handling or stopped intentionally
                }

                CleanupTunnelResources(config.Id);
                SetStatus(config.Id, TunnelStatus.Reconnecting, "Connection dropped. Reconnecting...");
                Log(config.Id, config.Name, "Connection lost. Scheduling automatic reconnection in 5 seconds...", isError: true);

                var cts = new CancellationTokenSource();
                _reconnectTokens[config.Id] = cts;

                Task.Run(() => ReconnectLoopAsync(config, cts.Token));
            }
        }

        private async Task ReconnectLoopAsync(TunnelConfig config, CancellationToken token)
        {
            int attempt = 0;
            int delaySeconds = 5;

            while (!token.IsCancellationRequested)
            {
                attempt++;
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (token.IsCancellationRequested) return;

                Log(config.Id, config.Name, $"Reconnection attempt #{attempt}...");

                // 1. Double check local port conflict before retrying
                if (config.Type == TunnelType.Local || config.Type == TunnelType.Dynamic)
                {
                    if (IsPortOccupied(config.LocalBindAddress, config.LocalPort, out _))
                    {
                        Log(config.Id, config.Name, $"Reconnection skipped: Local port {config.LocalPort} is busy.", isError: true);
                        continue; // Wait and try again in the next cycle
                    }
                }

                // 2. Perform connection attempt
                try
                {
                    await ConnectAndSetupTunnelAsync(config, token);
                    
                    // If connection succeeded, ConnectAndSetupTunnelAsync updates status to Running and loop ends
                    if (GetStatus(config.Id) == TunnelStatus.Running)
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log(config.Id, config.Name, $"Reconnection attempt #{attempt} failed: {ex.Message}", isError: true);
                }

                // Exponential backoff up to 60 seconds
                delaySeconds = Math.Min(delaySeconds * 2, 60);
            }
        }

        private void CleanupTunnelResources(string tunnelId)
        {
            // Stop forwarded ports
            if (_forwardedPorts.TryRemove(tunnelId, out var ports))
            {
                foreach (var port in ports)
                {
                    try
                    {
                        if (port.IsStarted)
                        {
                            port.Stop();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error stopping port listener: {ex.Message}");
                    }
                }
            }

            // Dispose SSH Client
            if (_sshClients.TryRemove(tunnelId, out var client))
            {
                try
                {
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                    }
                }
                catch { /* Ignore */ }

                try
                {
                    client.Dispose();
                }
                catch { /* Ignore */ }
            }

            // Remove password from memory when stopping the tunnel
            _sessionPasswords.TryRemove(tunnelId, out _);
        }

        private void CancelReconnectTask(string tunnelId)
        {
            if (_reconnectTokens.TryRemove(tunnelId, out var cts))
            {
                try
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                catch { /* Ignore */ }
            }
        }

        private void SetStatus(string tunnelId, TunnelStatus status, string message)
        {
            _statuses[tunnelId] = status;
            StatusChanged?.Invoke(this, new TunnelStatusEventArgs(tunnelId, status, message));
        }

        private void Log(string tunnelId, string tunnelName, string message, bool isError = false)
        {
            LogReceived?.Invoke(this, new TunnelLogEventArgs(tunnelId, tunnelName, message, isError));
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var id in _sshClients.Keys)
                {
                    CancelReconnectTask(id);
                    CleanupTunnelResources(id);
                }
            }
        }
    }
}
