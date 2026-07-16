using System;

namespace SPF.Models
{
    /// <summary>
    /// Type of SSH Port Forwarding tunnel.
    /// </summary>
    public enum TunnelType
    {
        Local,   // -L Local port forwarding
        Remote,  // -R Remote port forwarding
        Dynamic  // -D Dynamic SOCKS proxy
    }

    /// <summary>
    /// Authentication method used for SSH connection.
    /// </summary>
    public enum SshAuthMethod
    {
        Password,
        PrivateKey
    }

    /// <summary>
    /// Represents the configuration options for a single SSH forwarding tunnel.
    /// </summary>
    public class TunnelConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        public string Name { get; set; } = string.Empty;
        
        public TunnelType Type { get; set; } = TunnelType.Local;
        
        // Local binding adapter address (e.g. 127.0.0.1, 0.0.0.0, or specific NIC IP)
        public string LocalBindAddress { get; set; } = "127.0.0.1";
        
        public int LocalPort { get; set; }
        
        // Remote destination server (e.g. localhost, db-server.internal) - Unused for Dynamic
        public string RemoteHost { get; set; } = string.Empty;
        
        public int RemotePort { get; set; }
        
        // SSH server details
        public string SshHost { get; set; } = string.Empty;
        
        public int SshPort { get; set; } = 22;
        
        public string SshUser { get; set; } = string.Empty;
        
        // Authentication details
        public SshAuthMethod AuthMethod { get; set; } = SshAuthMethod.Password;
        
        public string SshKeyPath { get; set; } = string.Empty;
        
        public string SshKeyPassphrase { get; set; } = string.Empty;
        
        // Tunnel management features
        public bool AutoReconnect { get; set; } = true;
    }
}
