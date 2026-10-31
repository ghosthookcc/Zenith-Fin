namespace ZenithFin.PostgreSQL.Models.Entities
{
    public sealed class RecurringPlanItemRow
    {
        public long Id { get; set; }
        public string? Direction { get; set; }
        public string? Name { get; set; }
        public decimal? Amount { get; set; }
        public int? Every { get; set; }
        public string? Unit { get; set; }
        public string? LastDate { get; set; }
        public string? EndDate { get; set; }
        public string? SeriesKey { get; set; }
    }
}
