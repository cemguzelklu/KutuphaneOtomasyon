namespace KutuphaneOtomasyon.Services.Chat
{
    public record ChatReply(string Text, bool FromFallback, string? Provider = null);

    public interface IChatService
    {
        Task<ChatReply> AskAsync(string userId, string message, CancellationToken ct = default);
    }
}
