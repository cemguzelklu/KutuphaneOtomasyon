using KutuphaneOtomasyon.ViewModels.Members;

namespace KutuphaneOtomasyon.Services.AI
{
    public class NullAiAssistant : IAiAssistant
    {
        public Task<List<AiSuggestionVm>> RerankAndExplainAsync(
            int memberId, IEnumerable<AiSuggestionVm> candidates, CancellationToken ct = default)
            => Task.FromResult(candidates.ToList());

        public Task<string> SummarizeRiskAsync(
            MemberVm member, RiskResultVm risk, CancellationToken ct = default)
            => Task.FromResult("AI kapalı: basit risk özeti gösteriliyor.");
    }
}