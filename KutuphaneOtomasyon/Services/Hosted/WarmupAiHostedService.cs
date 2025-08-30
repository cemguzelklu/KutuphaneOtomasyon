using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using KutuphaneOtomasyon.Services.AI;   // IAiLogService
using KutuphaneOtomasyon.Models;       // AiLog

namespace KutuphaneOtomasyon.Services.Hosted
{
    public class WarmupAiHostedService : BackgroundService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _cfg;
        private readonly ILogger<WarmupAiHostedService> _log;
        private readonly IServiceScopeFactory _scopeFactory;

        public WarmupAiHostedService(
            IHttpClientFactory httpFactory,
            IConfiguration cfg,
            ILogger<WarmupAiHostedService> log,
            IServiceScopeFactory scopeFactory)
        {
            _httpFactory = httpFactory;
            _cfg = cfg;
            _log = log;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Uygulama açıldıktan kısa süre sonra 1 kez “ısındır”
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

            var baseUrl = (_cfg["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1").TrimEnd('/');
            var model = _cfg["OpenAI:Model"] ?? "llama3.1:latest";
            var isOllama = baseUrl.Contains("localhost:11434", StringComparison.OrdinalIgnoreCase);
            var root = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? baseUrl[..^3] : baseUrl;

            string endpoint = isOllama ? "/api/chat" : "/chat/completions";
            string url = isOllama ? $"{root}{endpoint}" : $"{baseUrl}{endpoint}";

            try
            {
                var client = _httpFactory.CreateClient("fast-http");

                object payload = isOllama
                    ? new
                    {
                        model,
                        keep_alive = "30m",
                        stream = false,
                        messages = new object[] { new { role = "user", content = "ping" } },
                        options = new
                        {
                            num_predict = 1,
                            num_ctx = 1024,
                            num_thread = Environment.ProcessorCount,
                            temperature = 0.0
                        }
                    }
                    : new
                    {
                        model,
                        stream = false,
                        max_tokens = 1,
                        messages = new object[] { new { role = "user", content = "ping" } }
                    };

                var json = JsonSerializer.Serialize(payload);
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                var apiKey = _cfg["OpenAI:ApiKey"];
                if (!isOllama && !string.IsNullOrWhiteSpace(apiKey))
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var sw = Stopwatch.StartNew();
                using var res = await client.SendAsync(req, stoppingToken);
                var raw = await res.Content.ReadAsStringAsync(stoppingToken);
                sw.Stop();

                var provider = isOllama ? "Ollama" : "OpenAI";

                // Konsola/Output'a bilgi düş
                _log.LogInformation("[AI Warmup] Provider={Provider} Url={Url} Status={Status} Elapsed={Ms}ms",
                    provider, url, (int)res.StatusCode, (int)sw.ElapsedMilliseconds);

                // (Opsiyonel ama faydalı) AiLogs tablosuna WARMUP kaydı at
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var logs = scope.ServiceProvider.GetRequiredService<IAiLogService>();
                    await logs.LogAsync(new AiLog
                    {
                        Action = "WARMUP",
                        Provider = provider,
                        Model = model,
                        Endpoint = endpoint,
                        HttpStatus = (int)res.StatusCode,
                        Success = res.IsSuccessStatusCode,
                        LatencyMs = (int)sw.ElapsedMilliseconds,
                        RequestPayload = json.Length > 4000 ? json.Substring(0, 4000) + " …" : json,
                        ResponsePayload = raw.Length > 4000 ? raw.Substring(0, 4000) + " …" : raw,
                        CreatedAtUtc = DateTime.UtcNow
                    }, stoppingToken);
                }
                catch (Exception exLog)
                {
                    _log.LogWarning(exLog, "[AI Warmup] AiLogs yazılamadı.");
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[AI Warmup] Başarısız.");
            }
        }
    }
}
