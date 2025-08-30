using System.Text;

namespace KutuphaneOtomasyon.Services.Chat
{
    public class SmartChatService : IChatService
    {
        private readonly IChatService _primary;
        private readonly IChatService _fallback;
        private const int MaxChars = 1200; // ~300 token civarı

        public SmartChatService(IChatService primary, IChatService fallback)
        {
            _primary = primary;
            _fallback = fallback;
        }

        public async Task<ChatReply> AskAsync(string userId, string message, CancellationToken ct = default)
        {
            message = Truncate(message, MaxChars);

            try
            {
                return await _primary.AskAsync(userId, message, ct);
            }
            catch (Exception ex)
            {
                // 429 / insufficient_quota vb durumlarda otomatik düş
                if (IsQuotaOrRateLimit(ex))
                    return await _fallback.AskAsync(userId, message, ct);

                // başka hata varsa da kullanıcı boş kalmasın
                return await _fallback.AskAsync(userId, message, ct);
            }
        }

        private static bool IsQuotaOrRateLimit(Exception ex)
        {
            var s = ex.ToString().ToLowerInvariant();
            return s.Contains("429") || s.Contains("insufficient_quota") || s.Contains("rate limit");
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max) + " …";
        }
    }
}
