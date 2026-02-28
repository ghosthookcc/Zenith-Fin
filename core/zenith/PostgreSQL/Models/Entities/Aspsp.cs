using System.ComponentModel.DataAnnotations.Schema;

namespace ZenithFin.PostgreSQL.Models.Entities
{
    public class AspspAuthenticationEntity
    {
        [Column("active_session_id")] public Guid? ActiveSessionId { get; init; }
        [Column("state")] public string? State { get; init; }
        [Column("aspsp_name")] public string? AspspName { get; init; }
        [Column("aspsp_country")] public string? AspspCountry { get; init; }
        [Column("psu_type")] public string? AspspPsuType { get; init; }
        [Column("auth_expires_at")] public DateTime? AuthExpiresAt { get; init; } = DateTime.UtcNow.AddMinutes(30);
    }

    public class AspspBankConnectionEntity
    {
        [Column("active_session_id")] public Guid? ActiveSessionId { get; init; }
        [Column("aspsp_name")] public string? AspspName { get; init; }
        [Column("aspsp_country")] public string? AspspCountry { get; init; }
        [Column("psu_type")] public string? AspspPsuType { get; init; }
    }

    public class AspspBankingSessionEntity
    {
        [Column("active_session_id")] public Guid? ActiveSessionId { get; init; }
        [Column("aspsp_session_id")] public string? AspspSessionId { get; init; }
        [Column("aspsp_name")] public string? AspspName { get; init; }
        [Column("aspsp_country")] public string? AspspCountry { get; init; }
        [Column("psu_type")] public string? AspspPsuType { get; init; }
        [Column("consent_expires_at")] public DateTime? ConsentExpiresAt { get; init; }
    }
}
