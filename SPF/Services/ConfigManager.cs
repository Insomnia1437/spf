using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SPF.Models;

namespace SPF.Services
{
    /// <summary>
    /// Service responsible for managing tunnel configuration file persistence, backups, and fallback directories.
    /// </summary>
    public static class ConfigManager
    {
        private const string ConfigFileName = "tunnels.json";
        private static readonly string AppDirectoryConfigPath;
        private static readonly string AppDataConfigPath;
        private static readonly string ActiveConfigPath;

        static ConfigManager()
        {
            // Set base directories
            AppDirectoryConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);

            string appDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimplePortForwarder"
            );
            AppDataConfigPath = Path.Combine(appDataFolder, ConfigFileName);

            // Determine active configuration path based on write availability
            if (CanWriteToPath(AppDomain.CurrentDomain.BaseDirectory))
            {
                ActiveConfigPath = AppDirectoryConfigPath;
            }
            else
            {
                // Ensure AppData directory exists
                if (!Directory.Exists(appDataFolder))
                {
                    Directory.CreateDirectory(appDataFolder);
                }
                ActiveConfigPath = AppDataConfigPath;
            }
        }

        /// <summary>
        /// Retrieves the path of the active configuration file.
        /// </summary>
        public static string GetActiveConfigPath() => ActiveConfigPath;

        /// <summary>
        /// Loads the tunnels list from the active configuration file.
        /// </summary>
        public static List<TunnelConfig> LoadTunnels()
        {
            try
            {
                if (File.Exists(ActiveConfigPath))
                {
                    string json = File.ReadAllText(ActiveConfigPath);
                    var tunnels = JsonSerializer.Deserialize<List<TunnelConfig>>(json);
                    return tunnels ?? new List<TunnelConfig>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load configuration: {ex.Message}");
            }
            return new List<TunnelConfig>();
        }

        /// <summary>
        /// Saves the tunnels list to the active configuration file.
        /// </summary>
        public static void SaveTunnels(List<TunnelConfig> tunnels)
        {
            try
            {
                string json = JsonSerializer.Serialize(tunnels, new JsonSerializerOptions { WriteIndented = true });

                // Ensure parent directory exists (needed if AppData path is used and directory was deleted)
                string? directory = Path.GetDirectoryName(ActiveConfigPath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(ActiveConfigPath, json);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to save configuration to {ActiveConfigPath}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Exports the active configuration file to a selected target path.
        /// </summary>
        public static void ExportConfig(string targetPath, List<TunnelConfig> tunnels)
        {
            try
            {
                string json = JsonSerializer.Serialize(tunnels, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(targetPath, json);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to export config: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Imports a configuration file from a selected source path and returns the tunnels list.
        /// </summary>
        public static List<TunnelConfig> ImportConfig(string sourcePath)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException("Selected config file does not exist.");
                }

                string json = File.ReadAllText(sourcePath);
                var tunnels = JsonSerializer.Deserialize<List<TunnelConfig>>(json);
                return tunnels ?? throw new InvalidDataException("Configuration file was empty or invalid.");
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Failed to import config: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Verifies whether the application has write access to a given directory.
        /// </summary>
        private static bool CanWriteToPath(string path)
        {
            try
            {
                string testFile = Path.Combine(path, Path.GetRandomFileName());
                using (FileStream fs = File.Create(testFile, 1, FileOptions.DeleteOnClose))
                {
                    fs.WriteByte(0);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
