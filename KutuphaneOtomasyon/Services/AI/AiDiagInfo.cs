namespace KutuphaneOtomasyon.Services.AI
{
    public class AiDiagInfo
    {
        public bool Enabled { get; set; }
        public string Provider { get; set; } = "";
        public string? Model { get; set; }
        public int? LastLatencyMs { get; set; }
        public string? LastPromptSnippet { get; set; }
        public string? LastResponseSnippet { get; set; }
    }
}