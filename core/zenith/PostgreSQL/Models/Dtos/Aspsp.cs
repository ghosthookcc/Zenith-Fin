using System.ComponentModel.DataAnnotations.Schema;
using ZenithFin.PostgreSQL.Models.Entities;

namespace ZenithFin.PostgreSQL.Models.Dtos
{
    public class AspspBankConnectionDto
    {
        [Column("aspsp_session_id")] public string? AspspSessionId { get; init; }
        [Column("aspsp_name")] public string? AspspName { get; init; }
        [Column("aspsp_country")] public string? AspspCountry { get; init; }
        [Column("consent_expires_at")] public DateTime? ConsentExpiresAt { get; init; }
        [Column("status")] public BankStatus? Status { get; init; }
    }
}
