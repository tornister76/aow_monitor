using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;

namespace KsaowMonitor;

public class ConfigurationManager
{
    private readonly ILogger _logger;
    private ServiceConfiguration? _configuration;

    public ConfigurationManager(ILogger logger)
    {
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
        
        if (File.Exists(configPath))
        {
            _configuration = LoadConfigurationFromFile(configPath);
            DecryptPasswordIfNeeded(configPath);
        }
        else
        {
            _configuration = await CreateConfigurationFromApman();
            SaveConfigurationToFile(configPath, _configuration);
        }
    }

    public ServiceConfiguration GetConfiguration()
    {
        return _configuration ?? throw new InvalidOperationException("Configuration not initialized");
    }

    private Task<ServiceConfiguration> CreateConfigurationFromApman()
    {
        var apmanPath = FindApmanIniPath();
        if (string.IsNullOrEmpty(apmanPath))
            throw new FileNotFoundException("Could not find apman.ini file");

        var apmanConfig = ParseApmanIni(apmanPath);
        
        Console.WriteLine("Wprowadź hasło do bazy danych:");
        var password = Console.ReadLine() ?? "";
        
        Console.WriteLine("Wprowadź adres webhook:");
        var webhookUrl = Console.ReadLine() ?? "";

        var encryptedPassword = EncryptPassword(password);
        return Task.FromResult(new ServiceConfiguration
        {
            ConnectionString = BuildConnectionString(apmanConfig, "{{PASSWORD}}"),
            DatabaseType = apmanConfig.DbType,
            WebhookUrl = webhookUrl,
            EncryptedPassword = encryptedPassword
        });
    }

    public string? FindApmanIniPathPublic() => FindApmanIniPath();
    
    private string? FindApmanIniPath()
    {
        try
        {
            using var registryKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\KAMSOFT\KS-APW");
            var programPath = registryKey?.GetValue("sciezka") as string;

            if (!string.IsNullOrEmpty(programPath))
            {
                var apmanPath = Path.Combine(programPath, "KS", "APW", "apman.ini");
                if (File.Exists(apmanPath))
                    return apmanPath;
            }

            var possiblePaths = new[]
            {
                @"C:\KS\APW\apman.ini",
                @"D:\KS\APW\apman.ini",
                @"E:\KS\APW\apman.ini",
                @"F:\KS\APW\apman.ini"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            var drives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName);

            foreach (var drive in drives)
            {
                try
                {
                    var ksPath = Path.Combine(drive, "KS", "APW", "apman.ini");
                    if (File.Exists(ksPath))
                        return ksPath;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Cannot access drive {Drive}", drive);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding apman.ini");
            return null;
        }
    }

    public ApmanConfiguration ParseApmanIniPublic(string apmanPath) => ParseApmanIni(apmanPath);
    
    private ApmanConfiguration ParseApmanIni(string apmanPath)
    {
        var lines = File.ReadAllLines(apmanPath, Encoding.GetEncoding("windows-1250"));
        var sections = new Dictionary<string, Dictionary<string, string>>();
        var currentSection = "";

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                currentSection = trimmed[1..^1];
                sections[currentSection] = new Dictionary<string, string>();
            }
            else if (trimmed.Contains('=') && !string.IsNullOrEmpty(currentSection))
            {
                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2)
                {
                    sections[currentSection][parts[0].Trim()] = parts[1].Trim();
                }
            }
        }

        if (!sections.ContainsKey("PARAMETRY") || !sections["PARAMETRY"].ContainsKey("ALIAS_BAZY"))
            throw new InvalidOperationException("Missing PARAMETRY section or ALIAS_BAZY parameter");

        var aliasName = sections["PARAMETRY"]["ALIAS_BAZY"];
        if (!sections.ContainsKey(aliasName))
            throw new InvalidOperationException($"Missing database section [{aliasName}]");

        var dbSection = sections[aliasName];
        return new ApmanConfiguration
        {
            DbType = dbSection.GetValueOrDefault("DB_TYPE", ""),
            DbServer = dbSection.GetValueOrDefault("DB_SERVER", ""),
            DbUser = dbSection.GetValueOrDefault("DB_USER", ""),
            DbPath = dbSection.GetValueOrDefault("DB_PATH", "")
        };
    }

    public string BuildConnectionStringPublic(ApmanConfiguration apman, string password) => BuildConnectionString(apman, password);
    
    private string BuildConnectionString(ApmanConfiguration apman, string password)
    {
        return apman.DbType.ToUpper() switch
        {
            "ORACLE" => $"oracle://{apman.DbUser}:{password}@{apman.DbServer}",
            "FB" => BuildFirebirdConnectionString(apman, password),
            _ => throw new NotSupportedException($"Unsupported database type: {apman.DbType}")
        };
    }

    private string BuildFirebirdConnectionString(ApmanConfiguration apman, string password)
    {
        var server = apman.DbServer;
        var port = "3050";
        
        if (server.Contains(':'))
        {
            var parts = server.Split(':');
            server = parts[0];
            port = parts[1];
        }

        return $"fb://{apman.DbUser}:{password}@{server}:{port}/{apman.DbPath}";
    }

    private ServiceConfiguration LoadConfigurationFromFile(string configPath)
    {
        var lines = File.ReadAllLines(configPath);
        var config = new ServiceConfiguration();

        foreach (var line in lines)
        {
            if (line.Contains('='))
            {
                var parts = line.Split('=', 2);
                var key = parts[0].Trim();
                var value = parts[1].Trim();

                switch (key.ToUpper())
                {
                    case "CONNECTION_STRING":
                        config.ConnectionString = value;
                        break;
                    case "DATABASE_TYPE":
                        config.DatabaseType = value;
                        break;
                    case "WEBHOOK_URL":
                        config.WebhookUrl = value;
                        break;
                    case "ENCRYPTED_PASSWORD":
                        config.EncryptedPassword = value;
                        break;
                }
            }
        }

        return config;
    }

    public void SaveConfigurationToFilePublic(string configPath, ServiceConfiguration config) => SaveConfigurationToFile(configPath, config);
    
    private void SaveConfigurationToFile(string configPath, ServiceConfiguration config)
    {
        var content = $@"CONNECTION_STRING={config.ConnectionString}
DATABASE_TYPE={config.DatabaseType}
WEBHOOK_URL={config.WebhookUrl}
ENCRYPTED_PASSWORD={config.EncryptedPassword}";

        File.WriteAllText(configPath, content);
    }

    private void DecryptPasswordIfNeeded(string configPath)
    {
        var lines = File.ReadAllLines(configPath);
        var needsUpdate = false;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("ENCRYPTED_PASSWORD=") && !lines[i].Contains("$AQ"))
            {
                var plainPassword = lines[i].Split('=', 2)[1];
                var encryptedPassword = EncryptPassword(plainPassword);
                lines[i] = $"ENCRYPTED_PASSWORD={encryptedPassword}";
                needsUpdate = true;
            }
        }

        if (needsUpdate)
        {
            File.WriteAllLines(configPath, lines);
        }
    }

    public string EncryptPasswordPublic(string password) => EncryptPassword(password);
    
    private string EncryptPassword(string password)
    {
        var data = Encoding.UTF8.GetBytes(password);
        var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.LocalMachine);
        return "$AQ" + Convert.ToBase64String(encrypted);
    }

    public string DecryptPassword(string encryptedPassword)
    {
        if (!encryptedPassword.StartsWith("$AQ"))
            return encryptedPassword;

        var base64Data = encryptedPassword[3..];
        var encrypted = Convert.FromBase64String(base64Data);
        var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(decrypted);
    }
}

public class ServiceConfiguration
{
    public string ConnectionString { get; set; } = "";
    public string DatabaseType { get; set; } = "";
    public string WebhookUrl { get; set; } = "";
    public string EncryptedPassword { get; set; } = "";
}

public class ApmanConfiguration
{
    public string DbType { get; set; } = "";
    public string DbServer { get; set; } = "";
    public string DbUser { get; set; } = "";
    public string DbPath { get; set; } = "";
}