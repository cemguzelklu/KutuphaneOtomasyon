using System.Threading;
using System.Threading.Tasks;
using KutuphaneOtomasyon.Models;

namespace KutuphaneOtomasyon.Services.AI
{
    public interface IAiLogService
    {
        Task LogAsync(AiLog log, CancellationToken ct = default);
    }
}
