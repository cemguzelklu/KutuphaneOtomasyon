using System.Collections.Generic;
using KutuphaneOtomasyon.ViewModels.Members;

namespace KutuphaneOtomasyon.Services.Recommendations
{
    public interface IRecommendationService
    {
        List<AiSuggestionVm> RecommendForMember(int memberId, int take = 8);
    }
}