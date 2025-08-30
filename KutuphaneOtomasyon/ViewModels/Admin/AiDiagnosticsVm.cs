namespace KutuphaneOtomasyon.ViewModels.Admin
{
    using System.Collections.Generic;
    using KutuphaneOtomasyon.ViewModels.Members;

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
    }
}