using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KutuphaneOtomasyon.Services.AI;
using KutuphaneOtomasyon.Models;

namespace KutuphaneOtomasyon.Services.Chat
{
    public class OpenAiChatService : IChatService
    {
        private readonly IHttpClientFactory _http;
        private readonly IAiLogService _logs;
        private readonly IHttpContextAccessor _httpCtx;
        private readonly IConfiguration _cfg;

        // Genel (RERANK/REWRITE/SUMMARY tarafıyla uyum için tutuluyor)
        private readonly string _apiKey;
        private readonly string _model;     // genel default
        private readonly string _baseUrl;   // genel default

        // CHAT'e özel
        private readonly string _chatBaseUrl; // Chat’i farklı sağlayıcıya yönlendirmek için
        private readonly string _chatModel;   // Chat’e özel model

        public OpenAiChatService(
            IHttpClientFactory http,
            IConfiguration cfg,
            IAiLogService logs,
            IHttpContextAccessor httpCtx)
        {
            _http = http;
            _cfg = cfg;
            _logs = logs;
            _httpCtx = httpCtx;

            _apiKey = (cfg["OpenAI:ApiKey"] ?? "").Trim().Trim('"');              // <— trim + tırnak
            _baseUrl = ((cfg["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1")
                       .Trim()).TrimEnd('/');

            var cfgModel = cfg["OpenAI:Model"];
            _model = string.IsNullOrWhiteSpace(cfgModel)
                ? (_baseUrl.Contains("localhost:11434", StringComparison.OrdinalIgnoreCase)
                    ? "phi3:mini"     // genel defaultu hafif tut
                    : "gpt-4o-mini")
                : cfgModel;

            // --- CHAT’e özel ayarlar (hibrit için esas alanlar) ---
            _chatBaseUrl = ((cfg["OpenAI:ChatBaseUrl"] ?? _baseUrl)
               .Trim()).TrimEnd('/');
            _chatModel = cfg["OpenAI:Models:Chat"] ?? _model;
        }

        public async Task<ChatReply> AskAsync(string userId, string message, CancellationToken ct = default)
        {
            // Bu çağrı sadece CHAT için; chat’in nereye gittiğini _chatBaseUrl belirler
            var baseForChat = _chatBaseUrl;
            var endsWithV1 = baseForChat.EndsWith("/v1", StringComparison.OrdinalIgnoreCase);
            var isOllamaChat =
                baseForChat.Contains("localhost:11434", StringComparison.OrdinalIgnoreCase) ||
                baseForChat.Contains("127.0.0.1:11434", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(_apiKey) && !isOllamaChat)
                throw new InvalidOperationException("OpenAI ApiKey yok (CHAT bulutta).");

            var client = _http.CreateClient("fast-http");

            var url = isOllamaChat
                ? $"{(endsWithV1 ? baseForChat[..^3] : baseForChat)}/api/chat"
                : $"{baseForChat}/chat/completions";

            var model = _chatModel;

            // PAYLOAD
            object payloadObj = isOllamaChat
                ? new
                {
                    model,
                    keep_alive = "2h",
                    stream = false,
                    messages = new object[]
                    {
                        new { role = "system", content =
                            "Sen bir kütüphane asistanısın. Yanıt dili HER ZAMAN TÜRKÇE. " +
                            "Kısa ve net yanıt ver; soru belirsizse tek bir kısa netleştirme sorusu sor. " +
                            "Yanıtların yalın ve doğrudan olsun." },
                        new { role = "user",   content = message }
                    },
                    options = new
                    {
                        num_predict = 56,                // kalite için 32→56
                        num_ctx = 768,               // bağlamı biraz aç
                        num_thread = Math.Max(2, Environment.ProcessorCount / 2),
                        num_batch = 64,
                        temperature = 0.2,
                        top_k = 20,
                        top_p = 0.9,
                        repeat_penalty = 1.05,
                        stop = new[] { "\n\n", "```", "</s>" }
                    }
                }
                : new
                {
                    model,
                    messages = new object[]
                    {
                        new { role = "system", content =
                            "You are a helpful assistant for a library system. Output language: Turkish. " +
                            "Be concise; ask one short clarifying question only if needed." },
                        new { role = "user",   content = message }
                    },
                    temperature = 0.2,
                    max_tokens = 80,
                    stream = false
                };

            var bodyJson = JsonSerializer.Serialize(payloadObj);

            var provider = isOllamaChat ? "Ollama" : "OpenAI";
            var endpoint = isOllamaChat ? "/api/chat" : "/chat/completions";

            int? promptTok = null, completionTok = null, totalTok = null;
            var status = -1;
            var sw = Stopwatch.StartNew();

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                if (!isOllamaChat)
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(12));

                using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                status = (int)res.StatusCode;
                var raw = await res.Content.ReadAsStringAsync(cts.Token);
                sw.Stop();

                try
                {
                    using var docUsage = JsonDocument.Parse(raw);
                    promptTok = JInt(docUsage.RootElement, "usage", "prompt_tokens");
                    completionTok = JInt(docUsage.RootElement, "usage", "completion_tokens");
                    totalTok = JInt(docUsage.RootElement, "usage", "total_tokens");
                }
                catch { }

                await _logs.LogAsync(new AiLog
                {
                    Action = "CHAT",
                    MemberId = null,
                    Provider = provider,
                    Model = model,
                    Endpoint = endpoint,
                    HttpStatus = status,
                    Success = res.IsSuccessStatusCode,
                    LatencyMs = (int)sw.ElapsedMilliseconds,
                    CorrelationId = _httpCtx.HttpContext?.TraceIdentifier,
                    RequestPayload = Trunc(bodyJson, 4000),
                    ResponsePayload = Trunc(raw, 4000),
                    ErrorType = res.IsSuccessStatusCode ? null : "http_error",
                    ErrorCode = res.IsSuccessStatusCode ? null : status.ToString(),
                    PromptTokens = promptTok,
                    CompletionTokens = completionTok,
                    TotalTokens = totalTok
                }, ct);

                if (!res.IsSuccessStatusCode)
                    throw new HttpRequestException($"AI failed: {status} {raw}");

                var text = ParseAssistantContent(raw)?.Trim() ?? "";
                return new ChatReply(text, FromFallback: false, Provider: provider);
            }
            catch (OperationCanceledException oce)
            {
                sw.Stop();
                await _logs.LogAsync(new AiLog
                {
                    Action = "CHAT",
                    MemberId = null,
                    Provider = provider,
                    Model = model,
                    Endpoint = endpoint,
                    HttpStatus = 499,
                    Success = false,
                    LatencyMs = (int)sw.ElapsedMilliseconds,
                    CorrelationId = _httpCtx.HttpContext?.TraceIdentifier,
                    RequestPayload = Trunc(bodyJson, 4000),
                    ResponsePayload = "canceled: " + oce.Message,
                    ErrorType = "timeout",
                    ErrorCode = "499",
                    PromptTokens = promptTok,
                    CompletionTokens = completionTok,
                    TotalTokens = totalTok
                }, ct);

                throw new HttpRequestException("timeout (chat)", oce); // SmartChatService fallback için
            }
            catch (Exception ex)
            {
                sw.Stop();
                await _logs.LogAsync(new AiLog
                {
                    Action = "CHAT",
                    MemberId = null,
                    Provider = provider,
                    Model = model,
                    Endpoint = endpoint,
                    HttpStatus = status == -1 ? 0 : status,
                    Success = false,
                    LatencyMs = (int)sw.ElapsedMilliseconds,
                    CorrelationId = _httpCtx.HttpContext?.TraceIdentifier,
                    RequestPayload = Trunc(bodyJson, 4000),
                    ResponsePayload = ex.ToString(),
                    ErrorType = "exception",
                    ErrorCode = ex.GetType().Name,
                    PromptTokens = promptTok,
                    CompletionTokens = completionTok,
                    TotalTokens = totalTok
                }, ct);

                throw; // SmartChatService yakalayacak
            }
        }

        // === Helpers ===
        private static string Trunc(string s, int max)
            => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + " …");

        private static int? JInt(JsonElement root, params string[] path)
        {
            try
            {
                var cur = root;
                foreach (var p in path) cur = cur.GetProperty(p);
                return cur.ValueKind == JsonValueKind.Number ? cur.GetInt32() : (int?)null;
            }
            catch { return null; }
        }

        private static string? ParseAssistantContent(string raw)
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // OpenAI compatible
            if (root.TryGetProperty("choices", out var choices))
                return choices[0].GetProperty("message").GetProperty("content").GetString();

            // Ollama native
            if (root.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var c))
                return c.GetString();

            return null;
        }
    }
}
