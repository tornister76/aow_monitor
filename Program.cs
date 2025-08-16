using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Microsoft.Win32;
using System.Xml.Linq;

namespace KsaowMonitor;

class Program
{
    static async Task Main(string[] args)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "--console":
                    await RunAsConsole();
                    break;
                case "--auto":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: KsaowMonitor.exe --auto <webhook_url>");
                        return;
                    }
                    await RunAutoConfig(args[1]);
                    break;
                default:
                    await RunAsService();
                    break;
            }
        }
        else
        {
            Log.Information("Starting service mode with args: {Args}", string.Join(" ", args));
            await RunAsService();
        }
    }

    static async Task RunAsConsole()
    {
        var services = new ServiceCollection()
            .AddLogging(builder => builder.AddConsole())
            .BuildServiceProvider();

        var logger = services.GetRequiredService<ILogger<MonitorService>>();
        var configManager = new ConfigurationManager(logger);
        var databaseManager = new DatabaseManager(logger);
        var webhookSender = new WebhookSender(logger);

        Console.WriteLine("KS-AOW Database Monitor - Test");
        
        try
        {
            await configManager.InitializeAsync();
            var config = configManager.GetConfiguration();
            
            var decryptedPassword = configManager.DecryptPassword(config.EncryptedPassword);
            var firmIds = await databaseManager.GetFirmIdsAsync(config.ConnectionString, config.DatabaseType, decryptedPassword);
            
            var timestamp = DateTime.UtcNow;
            var payload = new
            {
                timestamp = timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                status = "success",
                firm_ids = firmIds,
                database_type = config.DatabaseType,
                message = "Database access successful",
                executionMode = "test"
            };

            await webhookSender.SendAsync(config.WebhookUrl, payload);
            Console.WriteLine($"Test zakończony pomyślnie. Znaleziono {firmIds.Count} rekordów FIRM.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test nieudany: {ex.Message}");
        }
        
    }

    static async Task RunAutoConfig(string webhookUrl)
    {
        var services = new ServiceCollection()
            .AddLogging(builder => builder.AddConsole())
            .BuildServiceProvider();

        var logger = services.GetRequiredService<ILogger<Program>>();
        var configManager = new ConfigurationManager(logger);

        Console.WriteLine("KS-AOW Database Monitor - Auto Configuration");
        
        try
        {
            var apmanPath = configManager.FindApmanIniPathPublic();
            if (string.IsNullOrEmpty(apmanPath))
            {
                Console.WriteLine("BŁĄD: Nie znaleziono pliku apman.ini");
                return;
            }

            var apmanConfig = configManager.ParseApmanIniPublic(apmanPath);
            
            if (!string.Equals(apmanConfig.DbUser, "apw_user", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"BŁĄD: DB_USER ({apmanConfig.DbUser}) nie jest 'apw_user'. Auto-konfiguracja wymaga apw_user.");
                return;
            }

            var clientId = GetLicenseClientId();
            if (string.IsNullOrEmpty(clientId))
            {
                Console.WriteLine("BŁĄD: Nie udało się odczytać ID klienta z licencja_aow.xml");
                return;
            }

            var autoPassword = $"apw_user{clientId}";
            Console.WriteLine($"Użyto automatycznego hasła: apw_user{clientId}");

            var config = new ServiceConfiguration
            {
                ConnectionString = configManager.BuildConnectionStringPublic(apmanConfig, "{{PASSWORD}}"),
                DatabaseType = apmanConfig.DbType,
                WebhookUrl = webhookUrl,
                EncryptedPassword = configManager.EncryptPasswordPublic(autoPassword)
            };

            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            configManager.SaveConfigurationToFilePublic(configPath, config);

            Console.WriteLine($"✓ Konfiguracja zapisana do: {configPath}");
            Console.WriteLine($"✓ Webhook URL: {webhookUrl}");
            Console.WriteLine($"✓ Baza danych: {apmanConfig.DbType}");
            Console.WriteLine("Auto-konfiguracja zakończona pomyślnie!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"BŁĄD: {ex.Message}");
        }
        
    }

    static string? GetLicenseClientId()
    {
        try
        {
            string? xmlPath = null;

            using var registryKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\KAMSOFT\KS-APW");
            var programPath = registryKey?.GetValue("sciezka") as string;

            if (!string.IsNullOrEmpty(programPath))
            {
                xmlPath = Path.Combine(programPath, "APW", "AP", "licencja_aow.xml");
                if (!File.Exists(xmlPath))
                    xmlPath = null;
            }

            if (string.IsNullOrEmpty(xmlPath))
            {
                var possiblePaths = new[]
                {
                    @"C:\KS\APW\AP\licencja_aow.xml",
                    @"D:\KS\APW\AP\licencja_aow.xml",
                    @"E:\KS\APW\AP\licencja_aow.xml",
                    @"F:\KS\APW\AP\licencja_aow.xml"
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        xmlPath = path;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(xmlPath) || !File.Exists(xmlPath))
                return null;

            string xmlContent;
            try
            {
                xmlContent = File.ReadAllText(xmlPath, System.Text.Encoding.UTF8);
            }
            catch (System.ArgumentException)
            {
                var encoding = System.Text.Encoding.GetEncoding("windows-1250");
                xmlContent = File.ReadAllText(xmlPath, encoding);
            }
            
            var doc = XDocument.Parse(xmlContent);
            var ns = XNamespace.Get("http://www.kamsoft.pl/ks");
            var idElement = doc.Descendants(ns + "id-knt-ks").FirstOrDefault();
            
            return idElement?.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static async Task RunAsService()
    {
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "ksaow-monitor-.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(logPath, 
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 2,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var builder = Host.CreateApplicationBuilder();
        
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "KS-AOW Database Monitor";
        });

        builder.Services.AddSerilog();
        builder.Services.AddHostedService<MonitorService>();

        var host = builder.Build();
        await host.RunAsync();
    }

}