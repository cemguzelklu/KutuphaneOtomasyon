using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KutuphaneOtomasyon.ViewModels.Members;

namespace KutuphaneOtomasyon.Services.AI
{
    public class OpenAiAssistant : IAiAssistant
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly string _apiKey;
        private readonly string _model;

        public OpenAiAssistant(IHttpClientFactory httpFactory, IConfiguration cfg)
        {
            _httpFactory = httpFactory;
            _apiKey = cfg["OpenAI:ApiKey"] ?? "";
            _model = cfg["OpenAI:Model"] ?? "gpt-4o-mini";
        }

        public async Task<List<AiSuggestionVm>> RerankAndExplainAsync(
            int memberId, IEnumerable<AiSuggestionVm> candidates, CancellationToken ct = default)
        {
            var list = candidates.ToList();
            if (string.IsNullOrWhiteSpace(_apiKey) || list.Count == 0) return list;

            var http = _httpFactory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var compact = list.Select(c => new { c.BookId, c.Title, c.Author, c.AvailableCopies }).ToList();
            var payload = new
            {
                model = _model,
                temperature = 0.2,
                messages = new object[]
                {
                    new { role = "system", content =
                        "You are a helpful library recommender. " +
                        "Re-rank given candidates and RETURN ONLY a JSON array (no prose) " +
                        "of up to 8 items with fields: bookId, title, author, reason. " +
                        "Use ONLY provided candidates, prefer AvailableCopies>0, diversify authors." },
                    new { role = "user", content =
                        $"MemberId: {memberId}\nCandidates: {JsonSerializer.Serialize(compact)}" }
                }
            };

            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return list;

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString();

            List<AiSuggestionVm>? ai;
            try { ai = JsonSerializer.Deserialize<List<AiSuggestionVm>>(content ?? "[]"); }
            catch { ai = null; }

            if (ai == null || ai.Count == 0) return list;

            // Eksik alanları orijinal listeden tamamla
            var map = list.ToDictionary(x => x.BookId);
            foreach (var s in ai)
            {
                if (map.TryGetValue(s.BookId, out var orig))
                {
                    s.ThumbnailUrl = orig.ThumbnailUrl;
                    s.AvailableCopies = orig.AvailableCopies;
                    if (string.IsNullOrWhiteSpace(s.Title)) s.Title = orig.Title;
                    if (string.IsNullOrWhiteSpace(s.Author)) s.Author = orig.Author;
                }
            }
            return ai;
        }

        public async Task<string> SummarizeRiskAsync(
            MemberVm member, RiskResultVm risk, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey)) return risk.Summary;

            var http = _httpFactory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var payload = new
            {
                model = _model,
                temperature = 0.2,
                messages = new object[]
                {
                    new { role = "system", content =
                        "Türkçe ve en fazla 2 kısa cümleyle üyenin riskini özetle. " +
                        "Risk Medium/High ise uygulanabilir bir aksiyon öner." },
                    new { role = "user", content =
                        $"Üye: {member.Name} (#{member.MemberId}) | Skor: {risk.Score} | Seviye: {risk.Level} | " +
                        $"Gecikmiş: {risk.OverdueCount} | Yaklaşan: {risk.DueSoonCount}" }
                }
            };

            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return risk.Summary;

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString();

            return string.IsNullOrWhiteSpace(content) ? risk.Summary : content.Trim();
        }
    }
}