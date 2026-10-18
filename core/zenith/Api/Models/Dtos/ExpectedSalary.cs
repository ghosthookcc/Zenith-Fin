namespace ZenithFin.Api.Models.Dtos
{
    public static class ExpectedSalaryDto
    {
        public sealed class ExpectedSalary
        {
            public string Month { get; set; } = string.Empty;
            public decimal Amount { get; set; }
        }

        public sealed class SaveExpectedSalaryRequest
        {
            public decimal? Amount { get; set; }
        }
    }
}
