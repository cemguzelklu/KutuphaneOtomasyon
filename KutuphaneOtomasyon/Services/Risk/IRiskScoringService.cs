namespace KutuphaneOtomasyon.Services.Risk
{
    public interface IRiskScoringService
    {
        ViewModels.Members.RiskResultVm Calculate(int memberId);
    }
}