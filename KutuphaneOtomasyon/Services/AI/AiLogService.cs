using System.Threading;
using System.Threading.Tasks;
using KutuphaneOtomasyon.Data;
using KutuphaneOtomasyon.Models;

namespace KutuphaneOtomasyon.Services.AI
{
    public class AiLogService : IAiLogService
    {
        private readonly LibraryContext _ctx;
        public AiLogService(LibraryContext ctx) => _ctx = ctx;

        public async Task LogAsync(AiLog log, CancellationToken ct = default)
        {
            _ctx.AiLogs.Add(log);
            await _ctx.SaveChangesAsync(ct);
        }
    }
}
