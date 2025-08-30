using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KutuphaneOtomasyon.ViewModels.Members;
using System.Diagnostics;
using System.Text.RegularExpressions;
using KutuphaneOtomasyon.Models; 
namespace KutuphaneOtomasyon.Services.AI
{
    public class OpenAiAssistant : IAiAssistant
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly string _apiKey;
        private readonly string _model;
        private int? _lastLatencyMs;
        private string? _lastPromptSnippet;
        private string? _lastResponseSnippet;
        private readonly IAiLogService _aiLogs;
        private readonly IHttpContextAccessor _http;
        private readonly string _baseUrl;
        private readonly IConfiguration _cfg;

        private bool IsOllama => _baseUrl.Contains("localhost:11434", StringComparison.OrdinalIgnoreCase);
        private string ServerRoot => _baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? _baseUrl[..^3] : _baseUrl;
        private string ProviderName =>
    _baseUrl.Contains("localhost:11434", StringComparison.OrdinalIgnoreCase) ? "Ollama" : "OpenAI";
        public OpenAiAssistant(IHttpClientFactory httpFactory, IConfiguration cfg,
                       IAiLogService aiLogs, IHttpContextAccessor http)
        {
            _httpFactory = httpFactory;
            _cfg = cfg;
            _aiLogs = aiLogs;
            _http = http;

            _apiKey = cfg["OpenAI:ApiKey"] ?? "";
            _baseUrl = (cfg["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1").TrimEnd('/');

            var cfgModel = cfg["OpenAI:Model"];
            _model = string.IsNullOrWhiteSpace(cfgModel)
                ? (_baseUrl.Contains("localhost:11434", StringComparison.OrdinalIgnoreCase)
                    ? "llama3.1:8b-instruct-q4_K_M"
                    : "gpt-4o-mini")
                : cfgModel;
        }
        private static string Trunc(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return s.Length <= max ? s : s.Substring(0, max) + " …";
        }

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
        public bool IsEnabled => !string.IsNullOrWhiteSpace(_apiKey);
        public Task<AiDiagInfo> DiagnosticsAsync(CancellationToken ct = default)
           => Task.FromResult(new AiDiagInfo
           {
               Enabled = IsEnabled,
               Provider = ProviderName,   // <-- DEĞİŞTİ
               Model = _model,
               LastLatencyMs = _lastLatencyMs,
               LastPromptSnippet = _lastPromptSnippet,
               LastResponseSnippet = _lastResponseSnippet
           });
        public async Task<List<AiSuggestionVm>> RerankAndExplainAsync(
       int memberId, IEnumerable<AiSuggestionVm> candidates, CancellationToken ct = default)
        {
            var list = candidates.ToList();
            if (!IsEnabled || list.Count == 0) return list;

            // Daha az aday → daha az input token
            var compact = list.Take(6)
                .Select(c => new { c.BookId, c.Title, c.Author, c.AvailableCopies })
                .ToList();

            var http = _httpFactory.CreateClient("fast-http");
            var url = IsOllama ? $"{ServerRoot}/api/chat" : $"{_baseUrl}/chat/completions";

            var messages = new object[]
 {
    new { role = "system", content =
        "Sen bir kütüphane öneri asistanısın. ÇIKIŞ DİLİ: TÜRKÇE. " +
        "Sadece minify edilmiş BİR JSON DİZİSİ döndür (prose yok), en fazla 6 öğe. " +
        "Her öğe SADECE şu alanları içersin: {\"bookId\": <number>, \"reason\": \"<8 kelimeyi geçmeyen TÜRKÇE>\"}. " +
        "Sadece verilen adayları kullan, AvailableCopies>0 olanları tercih et, yazar çeşitliliğini koru. " +
        "Başlık/yazar/metin KATMA; SADECE bookId ve reason üret. " +
        "Özel isimleri ve eser adlarını olduğu gibi KORU. İngilizce kelime KULLANMA." },
    new { role = "user", content =
        $"MemberId: {memberId}\nCandidates: {JsonSerializer.Serialize(compact)}" }
 };


            var model = ModelFor("Rerank");
            // Çıktı küçük – 48 token yeterli
            var payloadObj = IsOllama
                ? BuildOllamaPayload(model, messages, temperature: 0.1, maxTokens: 48)
                : BuildOpenAiPayload(model, messages, temperature: 0.1, maxTokens: 48);

            var payloadJson = JsonSerializer.Serialize(payloadObj);
            _lastPromptSnippet = Trunc(payloadJson, 800);

            var endpoint = IsOllama ? "/api/chat" : "/chat/completions";
            var log = new AiLog
            {
                CreatedAtUtc = DateTime.UtcNow,
                CorrelationId = _http.HttpContext?.TraceIdentifier,
                Action = "RERANK",
                MemberId = memberId,
                Provider = ProviderName,
                Model = model,
                Endpoint = endpoint,
                RequestPayload = Trunc(payloadJson, 1500), // log’u da küçült
                Success = false,
                HttpStatus = -1
            };

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                if (!IsOllama) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                req.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(15)); // net süre

                var sw = Stopwatch.StartNew();
                using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                var raw = await res.Content.ReadAsStringAsync(cts.Token);
                sw.Stop();

                _lastLatencyMs = (int)sw.ElapsedMilliseconds;
                _lastResponseSnippet = Trunc(raw, 1200);

                log.LatencyMs = _lastLatencyMs;
                log.HttpStatus = (int)res.StatusCode;
                log.ResponsePayload = Trunc(raw, 1500);
                log.Success = res.IsSuccessStatusCode;

                try
                {
                    using var d = JsonDocument.Parse(raw);
                    var root = d.RootElement;
                    log.PromptTokens = JInt(root, "usage", "prompt_tokens");
                    log.CompletionTokens = JInt(root, "usage", "completion_tokens");
                    log.TotalTokens = JInt(root, "usage", "total_tokens");
                }
                catch { }

                await _aiLogs.LogAsync(log, ct);
                if (!res.IsSuccessStatusCode) return list;

                var content = ParseAssistantContent(raw);
                var onlyJson = ExtractJsonArray(content);

                // Beklenen format: [{ "bookId": 123, "reason": "..." }, ...]
                using var parsed = JsonDocument.Parse(onlyJson);
                var result = new List<AiSuggestionVm>();
                var map = list.ToDictionary(x => x.BookId);

                foreach (var el in parsed.RootElement.EnumerateArray())
                {
                    var id = el.TryGetProperty("bookId", out var bid) ? bid.GetInt32() : (int?)null;
                    var reason = el.TryGetProperty("reason", out var r) ? r.GetString() : null;
                    if (id.HasValue && map.TryGetValue(id.Value, out var orig))
                    {
                        result.Add(new AiSuggestionVm
                        {
                            BookId = id.Value,
                            Title = orig.Title,
                            Author = orig.Author,
                            AvailableCopies = orig.AvailableCopies,
                            ThumbnailUrl = orig.ThumbnailUrl,
                            Reason = reason
                        });
                    }
                }

                return result.Count > 0 ? result : list;
            }
            catch (OperationCanceledException oce)
            {
                log.ErrorType = "timeout";
                log.ErrorCode = "499";
                log.HttpStatus = 499;
                log.ResponsePayload = $"canceled: {oce.Message}";
                try { await _aiLogs.LogAsync(log, ct); } catch { }
                return list;
            }
            catch (HttpRequestException hre)
            {
                log.ErrorType = "http_error";
                log.ErrorCode = hre.StatusCode?.ToString() ?? "http_request_exception";
                log.ResponsePayload = hre.Message;
                try { await _aiLogs.LogAsync(log, ct); } catch { }
                return list;
            }
            catch (Exception ex)
            {
                log.ErrorType = "exception";
                log.ErrorCode = ex.GetType().Name;
                log.ResponsePayload = ex.ToString();
                try { await _aiLogs.LogAsync(log, ct); } catch { }
                return list;
            }
        }


        public async Task<string> SummarizeRiskAsync(MemberVm member, RiskResultVm risk, CancellationToken ct = default)
        {
            if (!IsEnabled) return risk.Summary;

            var http = _httpFactory.CreateClient("fast-http");
            var url = IsOllama ? $"{ServerRoot}/api/chat" : $"{_baseUrl}/chat/completions";

            var messages = new object[]
            {
        new { role = "system", content =
            "Türkçe ve en fazla 2 kısa cümleyle üyenin riskini özetle. " +
            "Risk Medium/High ise 1 uygulanabilir aksiyon öner." },
        new { role = "user", content =
            $"Üye: {member.Name} (#{member.MemberId}) | Skor: {risk.Score} | Seviye: {risk.Level} | " +
            $"Gecikmiş: {risk.OverdueCount} | Yaklaşan: {risk.DueSoonCount}" }
            };

            var model = ModelFor("Summarize");
            var payloadObj = IsOllama
     ? BuildOllamaPayload(model, messages, temperature: 0.0, maxTokens: 32)
     : BuildOpenAiPayload(model, messages, temperature: 0.0, maxTokens: 32);

            var payloadJson = JsonSerializer.Serialize(payloadObj);
            _lastPromptSnippet = Trunc(payloadJson, 800);

            var endpoint = IsOllama ? "/api/chat" : "/chat/completions";
            var log = new AiLog
            {
                CreatedAtUtc = DateTime.UtcNow,
                CorrelationId = _http.HttpContext?.TraceIdentifier,
                Action = "RISK_SUMMARY",
                MemberId = member.MemberId,
                Provider = ProviderName,
                Model = model,
                Endpoint = endpoint,
                RequestPayload = Trunc(payloadJson, 4000),
                Success = false,
                HttpStatus = -1
            };

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                if (!IsOllama) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                req.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(12)); // <-- cts TANIMLANDIKTAN sonra!

                var sw = Stopwatch.StartNew();
                using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                var raw = await res.Content.ReadAsStringAsync(cts.Token);
                sw.Stop();

                _lastLatencyMs = (int)sw.ElapsedMilliseconds;
                _lastResponseSnippet = Trunc(raw, 1200);

                log.LatencyMs = _lastLatencyMs;
                log.HttpStatus = (int)res.StatusCode;
                log.ResponsePayload = Trunc(raw, 4000);
                log.Success = res.IsSuccessStatusCode;

                try
                {
                    using var d = JsonDocument.Parse(raw);
                    var root = d.RootElement;
                    log.PromptTokens = JInt(root, "usage", "prompt_tokens");
                    log.CompletionTokens = JInt(root, "usage", "completion_tokens");
                    log.TotalTokens = JInt(root, "usage", "total_tokens");
                }
                catch { }

                await _aiLogs.LogAsync(log, ct);
                if (!res.IsSuccessStatusCode) return risk.Summary;

                var content = ParseAssistantContent(raw);
                return string.IsNullOrWhiteSpace(content) ? risk.Summary : content.Trim();
            }
            catch (OperationCanceledException oce)
            {
                log.ErrorType = "timeout";
                log.ErrorCode = "499";
                log.HttpStatus = 499;
                log.ResponsePayload = $"canceled: {oce.Message}";
                try { await _aiLogs.LogAsync(log, ct); } catch { }
                return risk.Summary;
            }
            catch (HttpRequestException hre)
            {
                log.ErrorType = "http_error";
                log.ErrorCode = hre.StatusCode?.ToString() ?? "http_request_exception";
                log.ResponsePayload = hre.Message;
                try { await _aiLogs.LogAsync(log, ct); } catch { }
                return risk.Summary;
            }
            catch (Exception ex)
            {
                log.ErrorType = "exception";
                log.ErrorCode = ex.GetType().Name;
                log.ResponsePayload = ex.ToString();
                try { await _aiLogs.LogAsync(log, ct); } catch { }
                return risk.Summary;
            }
        }

        private static string ExtractJsonArray(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "[]";
            var s = text.Trim();

            // ```json ... ``` bloğu
            var m = Regex.Match(s, @"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
            if (m.Success) s = m.Groups[1].Value.Trim();

            // İlk '[' ile son ']' arası
            int i = s.IndexOf('['), j = s.LastIndexOf(']');
            if (i >= 0 && j > i) return s.Substring(i, j - i + 1).Trim();

            // Doğrudan dizi değilse boş dizi dön
            return "[]";
        }

        public async Task<string?> SuggestQueryRewriteAsync(string query, CancellationToken ct = default)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(query)) return null;

            var http = _httpFactory.CreateClient("fast-http");
            var url = IsOllama ? $"{ServerRoot}/api/chat" : $"{_baseUrl}/chat/completions";

            var messages = new object[]
  {
    new { role = "system", content =
        "Görevin: Kullanıcı kitap arama sorgusunu daha isabetli hale getir. " +
        "ÇIKIŞ DİLİ: Kullanıcının dili (Türkçe ise Türkçe). " +
        "Sadece DÜZ METİN döndür; başka hiçbir şey yazma. " +
        "Özel isimleri/kitap adlarını/yazarları ÇEVİRME, olduğu gibi bırak. " +
        "Eğer sorgu zaten iyi ise AYNI HALİYLE döndür." },
    new { role = "user", content = query }
  };

            var model = ModelFor("Rewrite");
            var payloadObj = IsOllama
      ? BuildOllamaPayload(model, messages, temperature: 0.0, maxTokens: 16)
      : BuildOpenAiPayload(model, messages, temperature: 0.0, maxTokens: 16);

            var payloadJson = JsonSerializer.Serialize(payloadObj);
            _lastPromptSnippet = Trunc(payloadJson, 800);

            var endpoint = IsOllama ? "/api/chat" : "/chat/completions";
            var log = new AiLog
            {
                CreatedAtUtc = DateTime.UtcNow,
                CorrelationId = _http.HttpContext?.TraceIdentifier,
                Action = "QUERY_REWRITE",
                MemberId = null,
                Provider = ProviderName,
                Model = model,
                Endpoint = endpoint,
                RequestPayload = Trunc(payloadJson, 2000),
                Success = false,
                HttpStatus = -1
            };

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                if (!IsOllama) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                req.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8)); // <-- cts TANIMLANDIKTAN sonra!

                var sw = Stopwatch.StartNew();
                using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                var raw = await res.Content.ReadAsStringAsync(cts.Token);
                sw.Stop();

                _lastLatencyMs = (int)sw.ElapsedMilliseconds;
                _lastResponseSnippet = Trunc(raw, 1200);

                log.LatencyMs = _lastLatencyMs;
                log.HttpStatus = (int)res.StatusCode;
                log.ResponsePayload = Trunc(raw, 2000);
                log.Success = res.IsSuccessStatusCode;

                try
                {
                    using var d = JsonDocument.Parse(raw);
                    var root = d.RootElement;
                    log.PromptTokens = JInt(root, "usage", "prompt_tokens");
                    log.CompletionTokens = JInt(root, "usage", "completion_tokens");
                    log.TotalTokens = JInt(root, "usage", "total_tokens");
                }
                catch { }

                await _aiLogs.LogAsync(log, ct);
                if (!res.IsSuccessStatusCode) return null;

                var content = ParseAssistantContent(raw);
                var cleaned = (content ?? "").Trim().Replace("```", "").Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(cleaned)) return null;
                if (string.Equals(cleaned, query, StringComparison.OrdinalIgnoreCase)) return null;

                return cleaned;
            }
            catch (OperationCanceledException oce)
            {
                log.ErrorType = "timeout";
                log.ErrorCode = "499";
                log.HttpStatus = 499;
                log.ResponsePayload = $"canceled: {oce.Message}";
                try { await _aiLogs.LogAsync(log, ct); } catch { }
                return null;
            }
            catch (HttpRequestException hre)
            {
                log.ErrorType = "http_error";
                log.ErrorCode = hre.StatusCode?.ToString() ?? "http_request_exception";
                log.ResponsePayload = hre.Message;
                try { await _aiLogs.LogAsync(log, ct); } catch { }
                return null;
            }
            catch (Exception ex)
            {
                log.ErrorType = "exception";
                log.ErrorCode = ex.GetType().Name;
                log.ResponsePayload = ex.ToString();
                try { await _aiLogs.LogAsync(log, ct); } catch { }
                return null;
            }
        }



        private object BuildPayload(object[] messages, double temperature, int maxTokens)
        {
            var payload = new Dictionary<string, object>
            {
                ["model"] = _model,
                ["temperature"] = temperature,
                ["messages"] = messages,
                ["max_tokens"] = maxTokens
            };

            if (IsOllama)
            {
                payload["extra_body"] = new
                {
                    num_predict = maxTokens,                  // üretilecek token üst sınırı
                    num_ctx = 1024,                       // daha küçük bağlam = daha hızlı
                    num_thread = Environment.ProcessorCount, // CPU çekirdeklerini kullan
                    temperature = temperature,
                    top_k = 40,
                    top_p = 0.9,
                    repeat_penalty = 1.1,
                    keep_alive = "30m"                       // model RAM/VRAM’de kalsın
                };
            }
            return payload;
        }

        private string ModelFor(string key)
        {
            var m = _cfg[$"OpenAI:Models:{key}"];
            if (!string.IsNullOrWhiteSpace(m)) return m;
            return _model;
        }

        private static string? ParseAssistantContent(string raw)
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.TryGetProperty("choices", out var choices))
                return choices[0].GetProperty("message").GetProperty("content").GetString(); // OpenAI compat

            if (root.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var c))
                return c.GetString(); // Ollama native

            return null;
        }

        private object BuildOpenAiPayload(string model, object[] messages, double temperature, int maxTokens)
    => new Dictionary<string, object>
    {
        ["model"] = model,
        ["temperature"] = temperature,
        ["messages"] = messages,
        ["max_tokens"] = maxTokens,
        ["stream"] = false
    };

        private object BuildOllamaPayload(string model, object[] messages, double temperature, int maxTokens)
    => new
    {
        model,
        keep_alive = "2h",
        stream = false,
        messages,
        options = new
        {
            // ÜRETİMİ NET KIS:
            num_predict = maxTokens,              // örn: Rerank 48-64, Summ 32-40, Rewrite 12
            num_ctx = 512,
            num_thread = Math.Max(2, Environment.ProcessorCount / 2),
            num_batch = 64,                     // CPU’da çoğu zaman hız kazandırır
            temperature = temperature,
            top_k = 20,
            top_p = 0.9,
            repeat_penalty = 1.05,
            // Erken durdurma:
            stop = new[] { "\n\n", "```", "</s>" }
        }
    };

    }
}