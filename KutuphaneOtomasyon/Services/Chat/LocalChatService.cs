namespace KutuphaneOtomasyon.Services.Chat
{
    public class LocalChatService : IChatService
    {
        public Task<ChatReply> AskAsync(string userId, string message, CancellationToken ct = default)
        {
            // Burada istersen mesaja göre kurallı yanıtlar verebilirsin.
            var reply =
                "Şu an yoğunluk var, kısa bir yanıtla yardımcı olayım:\n" +
                "• Çalışma saatleri: Hafta içi 09:00–18:00\n" +
                "• Gecikme politikası: 3 güne kadar uyarı, sonrası cezalı.\n" +
                "• Kitap arama için üstteki aramayı kullanabilirsin.";

            return Task.FromResult(new ChatReply(reply, FromFallback: true, Provider: "Local"));
        }
    }
}
