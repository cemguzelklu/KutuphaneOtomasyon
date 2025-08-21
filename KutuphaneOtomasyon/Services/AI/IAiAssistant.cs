using KutuphaneOtomasyon.ViewModels.Members;

namespace KutuphaneOtomasyon.Services.AI
{
    public interface IAiAssistant
    {
        Task<List<AiSuggestionVm>> RerankAndExplainAsync(
            int memberId, IEnumerable<AiSuggestionVm> candidates, CancellationToken ct = default);

        Task<string> SummarizeRiskAsync(
            MemberVm member, RiskResultVm risk, CancellationToken ct = default);
    }
}