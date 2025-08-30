namespace KutuphaneOtomasyon.ViewModels.Admin
{
    using KutuphaneOtomasyon.Models;
    using KutuphaneOtomasyon.ViewModels.Members;
    using System.Collections.Generic;

    public class AiDiagnosticsVm
    {
        public bool Enabled { get; set; }
        public string Provider { get; set; } = "";
        public string? Model { get; set; }
        public int? LastLatencyMs { get; set; }
        public string? LastPromptSnippet { get; set; }
        public string? LastResponseSnippet { get; set; }

        public int? TestMemberId { get; set; }
        public List<AiSuggestionVm>? TestAiResults { get; set; }
        public IEnumerable<AiRecommendationHistory>? SavedRecommendations { get; set; }
    }
}