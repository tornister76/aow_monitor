using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace KsaowMonitor;

public class WebhookSender
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;

    public WebhookSender(ILogger logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task SendAsync(string webhookUrl, object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            _logger.LogInformation("Sending webhook to {Url}", webhookUrl);
            
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync(webhookUrl, content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Webhook sent successfully. Response: {Response}", responseContent);
            }
            else
            {
                _logger.LogWarning("Webhook failed with status {StatusCode}: {ReasonPhrase}", 
                    response.StatusCode, response.ReasonPhrase);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error sending webhook to {Url}", webhookUrl);
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout sending webhook to {Url}", webhookUrl);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending webhook to {Url}", webhookUrl);
            throw;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}