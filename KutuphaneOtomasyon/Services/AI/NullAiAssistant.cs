using KutuphaneOtomasyon.ViewModels.Members;

namespace KutuphaneOtomasyon.Services.AI
{
    public class NullAiAssistant : IAiAssistant
    {

        public bool IsEnabled => false;
        public Task<List<AiSuggestionVm>> RerankAndExplainAsync(
            int memberId, IEnumerable<AiSuggestionVm> candidates, CancellationToken ct = default)
            => Task.FromResult(candidates.ToList());

        public Task<string> SummarizeRiskAsync(MemberVm member, RiskResultVm risk, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public Task<AiDiagInfo> DiagnosticsAsync(CancellationToken ct = default)
         => Task.FromResult(new AiDiagInfo
         {
             Enabled = false,
             Provider = "Null",
             Model = null,
             LastLatencyMs = null,
             LastPromptSnippet = null,
             LastResponseSnippet = null
         });

        public Task<string?> SuggestQueryRewriteAsync(string query, CancellationToken ct = default)
    => Task.FromResult<string?>(null);
    }
}