using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace KsaowMonitor;

public class MonitorService : BackgroundService
{
    private readonly ILogger<MonitorService> _logger;
    private readonly ConfigurationManager _configManager;
    private readonly DatabaseManager _databaseManager;
    private readonly WebhookSender _webhookSender;
    public new Task ExecuteTask { get; private set; } = Task.CompletedTask;

    public MonitorService(ILogger<MonitorService> logger)
    {
        _logger = logger;
        _configManager = new ConfigurationManager(_logger);
        _databaseManager = new DatabaseManager(_logger);
        _webhookSender = new WebhookSender(_logger);
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("KS-AOW Monitor Service starting...");
        await base.StartAsync(cancellationToken);
        _logger.LogInformation("KS-AOW Monitor Service started successfully");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("KS-AOW Monitor Service stopping...");
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("KS-AOW Monitor Service stopped");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ExecuteTask = ExecuteInternalAsync(stoppingToken);
        await ExecuteTask;
    }

    private async Task ExecuteInternalAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("KS-AOW Monitor Service main loop started");

        await InitializeConfiguration();

        while (!stoppingToken.IsCancellationRequested)
        {
            await PerformMonitoringCheck();
            _logger.LogInformation("Next check in 5 minutes...");
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
        
        _logger.LogInformation("KS-AOW Monitor Service main loop ended");
    }

    private async Task InitializeConfiguration()
    {
        try
        {
            _logger.LogInformation("Working directory: {Directory}", Directory.GetCurrentDirectory());
            _logger.LogInformation("Base directory: {Directory}", AppDomain.CurrentDomain.BaseDirectory);
            await _configManager.InitializeAsync();
            _logger.LogInformation("Configuration initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize configuration - service will continue with limited functionality");
        }
    }

    private async Task PerformMonitoringCheck()
    {
        var timestamp = DateTime.UtcNow;
        
        try
        {
            var config = _configManager.GetConfiguration();
            
            var decryptedPassword = _configManager.DecryptPassword(config.EncryptedPassword);
            var firmIds = await _databaseManager.GetFirmIdsAsync(config.ConnectionString, config.DatabaseType, decryptedPassword);
            
            var payload = new
            {
                timestamp = timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                status = "success",
                firm_ids = firmIds,
                database_type = config.DatabaseType,
                message = "Database access successful",
                executionMode = "production"
            };

            await _webhookSender.SendAsync(config.WebhookUrl, payload);
            _logger.LogInformation("Monitoring check completed successfully. Found {Count} FIRM records", firmIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database monitoring check failed");
            
            try
            {
                var config = _configManager.GetConfiguration();
                var payload = new
                {
                    timestamp = timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    status = "error",
                    firm_ids = new List<object>(),
                    database_type = config.DatabaseType,
                    message = ex.Message,
                    executionMode = "production"
                };

                await _webhookSender.SendAsync(config.WebhookUrl, payload);
            }
            catch (Exception webhookEx)
            {
                _logger.LogError(webhookEx, "Failed to send error webhook");
            }
        }
    }
}