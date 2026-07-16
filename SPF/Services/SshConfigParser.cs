using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SPF.Services
{
    /// <summary>
    /// Represents SSH host settings parsed from standard ~/.ssh/config.
    /// </summary>
    public class SshHostInfo
    {
        public string Alias { get; set; } = string.Empty;
        public string HostName { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
        public int Port { get; set; } = 22;
        public string IdentityFile { get; set; } = string.Empty;
    }

    /// <summary>
    /// Service that reads and parses standard OpenSSH client configurations.
    /// </summary>
    public static class SshConfigParser
    {
        /// <summary>
        /// Reads ~/.ssh/config and returns a list of parsed host configurations.
        /// </summary>
        public static List<SshHostInfo> ParseSshConfig()
        {
            var hostsList = new List<SshHostInfo>();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string configPath = Path.Combine(userProfile, ".ssh", "config");

            if (!File.Exists(configPath))
            {
                return hostsList;
            }

            try
            {
                string[] lines = File.ReadAllLines(configPath);
                SshHostInfo? currentHost = null;

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    
                    // Skip comments and empty lines
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    {
                        continue;
                    }

                    // OpenSSH config parameters can be space-separated or equals-separated
                    string[] tokens = trimmed.Split(new[] { ' ', '=' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length < 2)
                    {
                        continue;
                    }

                    string key = tokens[0].ToLowerInvariant();
                    string value = tokens[1].Trim('\"', '\'').Trim();

                    if (key == "host")
                    {
                        // Save previous host if valid
                        if (currentHost != null && !string.IsNullOrEmpty(currentHost.Alias) && !currentHost.Alias.Contains("*"))
                        {
                            hostsList.Add(currentHost);
                        }

                        currentHost = new SshHostInfo
                        {
                            Alias = value
                        };
                    }
                    else if (currentHost != null)
                    {
                        switch (key)
                        {
                            case "hostname":
                                currentHost.HostName = value;
                                break;
                            case "user":
                                currentHost.User = value;
                                break;
                            case "port":
                                if (int.TryParse(value, out int port))
                                {
                                    currentHost.Port = port;
                                }
                                break;
                            case "identityfile":
                                currentHost.IdentityFile = ResolveSshPath(value);
                                break;
                        }
                    }
                }

                // Add the final host entry
                if (currentHost != null && !string.IsNullOrEmpty(currentHost.Alias) && !currentHost.Alias.Contains("*"))
                {
                    hostsList.Add(currentHost);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to parse ssh config: {ex.Message}");
            }

            return hostsList;
        }

        /// <summary>
        /// Expands the SSH path shortcuts (like ~ or ~/.ssh) to full paths.
        /// </summary>
        public static string ResolveSshPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Replace standard Unix home shortcuts with Windows user profile folder
            if (path.StartsWith("~/"))
            {
                path = Path.Combine(userProfile, path.Substring(2).Replace('/', '\\'));
            }
            else if (path.StartsWith("~"))
            {
                path = Path.Combine(userProfile, path.Substring(1).Replace('/', '\\'));
            }

            // If it's a relative path, default it to ~/.ssh/ folder
            if (!Path.IsPathRooted(path))
            {
                string sshFolder = Path.Combine(userProfile, ".ssh");
                string combined = Path.Combine(sshFolder, path);
                if (File.Exists(combined))
                {
                    return combined;
                }
            }

            return path;
        }
    }
}
