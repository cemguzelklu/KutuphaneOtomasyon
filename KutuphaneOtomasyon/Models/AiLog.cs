using System;
using System.ComponentModel.DataAnnotations;

namespace KutuphaneOtomasyon.Models
{
    public class AiLog
    {
        public int AiLogId { get; set; }

        // Zaman & korelasyon
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        [MaxLength(64)] public string? CorrelationId { get; set; }  // HttpContext.TraceIdentifier

        // İş türü
        [MaxLength(32)] public string Action { get; set; } = "";    // "RERANK" | "RISK_SUMMARY"
        public int? MemberId { get; set; }                          // ilgili üye (varsa)

        // Sağlayıcı/model
        [MaxLength(32)] public string Provider { get; set; } = "";  // "OpenAI"
        [MaxLength(64)] public string? Model { get; set; }          // "gpt-4o-mini"
        [MaxLength(64)] public string? Endpoint { get; set; }       // "/v1/chat/completions"

        // İstek/yanıt (kısaltılmış)
        public string? RequestPayload { get; set; }                 // nvarchar(max)
        public string? ResponsePayload { get; set; }                // nvarchar(max)

        // Ölçümler
        public int? LatencyMs { get; set; }
        public bool Success { get; set; }
        public int? HttpStatus { get; set; }
        [MaxLength(64)] public string? ErrorType { get; set; }      // "insufficient_quota" vb.
        [MaxLength(64)] public string? ErrorCode { get; set; }      // "insufficient_quota"

        // Token kullanımı (varsa)
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
        public int? TotalTokens { get; set; }
    }
}
