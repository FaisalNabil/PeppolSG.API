using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using log4net;

namespace PeppolSG.API.Configuration
{
    /// <summary>
    /// Central configuration provider with startup validation, secure value decrypt and hot-reload.
    /// </summary>
    public static class ConfigurationService
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(ConfigurationService));
        private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>();
        private static readonly string[] _mandatoryKeys = new[]
        {
            "PeppolP12FilePath",
            "PeppolP12PasswordEncrypted",
            "IsTestEnvironment"
        };
        private static FileSystemWatcher _watcher;
        private static readonly object _sync = new object();
        private static bool _initialised;

        public static void Initialize()
        {
            lock (_sync)
            {
                if (_initialised) return;
                LoadCache();
                ValidateMandatoryKeys();
                SetupHotReload();
                log.Info("ConfigurationService initialised (env=" + EnvironmentName + ")");
                _initialised = true;
            }
        }

        public static string EnvironmentName =>
            Environment.GetEnvironmentVariable("APP_ENV")
            ?? GetString("Environment")
            ?? "Production";

        public static string GetString(string key, string defaultVal = null)
        {
            if (!_initialised) Initialize();
            return _cache.TryGetValue(key, out var v) ? v : defaultVal;
        }

        public static int GetInt(string key, int defaultVal = 0) => int.TryParse(GetString(key), out var v) ? v : defaultVal;
        public static bool GetBool(string key, bool defaultVal = false) => bool.TryParse(GetString(key), out var v) ? v : defaultVal;

        private static void LoadCache()
        {
            _cache.Clear();
            foreach (var key in ConfigurationManager.AppSettings.AllKeys)
            {
                var val = ConfigurationManager.AppSettings[key];
                if (key.EndsWith("Encrypted", StringComparison.OrdinalIgnoreCase))
                    val = Decrypt(val);
                _cache[key] = val;
            }
        }

        private static void ValidateMandatoryKeys()
        {
            var missing = new List<string>();
            foreach (var k in _mandatoryKeys)
            {
                if (!_cache.ContainsKey(k) || string.IsNullOrWhiteSpace(_cache[k]))
                    missing.Add(k);
            }
            if (missing.Count > 0)
            {
                var msg = "Missing mandatory configuration keys: " + string.Join(", ", missing);
                log.Fatal(msg);
                throw new ConfigurationErrorsException(msg);
            }
        }

        private static string Decrypt(string cipher)
        {
            try
            {
                var bytes = Convert.FromBase64String(cipher);
                var plainBytes = ProtectedData.Unprotect(bytes, null, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                log.Error("Failed to decrypt configuration value", ex);
                throw;
            }
        }

        private static void SetupHotReload()
        {
            var configPath = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(configPath)!, Path.GetFileName(configPath)!);
            _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
            _watcher.Changed += (_, __) => ReloadDebounced();
            _watcher.EnableRaisingEvents = true;
        }

        private static Timer _reloadTimer;
        private static void ReloadDebounced()
        {
            // debounce multiple change events within 3s
            _reloadTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _reloadTimer = new Timer(_ => Reload(), null, 3000, Timeout.Infinite);
        }

        private static void Reload()
        {
            lock (_sync)
            {
                log.Info("Configuration file change detected – reloading");
                ConfigurationManager.RefreshSection("appSettings");
                LoadCache();
            }
        }
    }
} 