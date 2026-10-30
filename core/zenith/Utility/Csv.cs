using System.Globalization;
using System.Text;

namespace ZenithFin.Utility
{
    public sealed record CsvDialect(string Format,
                                    char Delimiter,
                                    string DecimalSeparator,
                                    bool ByteOrderMark)
    {
        public static readonly CsvDialect Excel = new("excel", ';', ",", true);
        public static readonly CsvDialect Standard = new("standard", ',', ".", false);

        public static CsvDialect? FromFormat(string? format)
        {
            return format?.Trim().ToLowerInvariant() switch
            {
                null or "" or "excel" => Excel,
                "standard" => Standard,
                _ => null
            };
        }
    }

    public readonly struct CsvCell
    {
        private static readonly char[] FormulaStarts = { '=', '+', '-', '@', '\t', '\r' };

        private readonly string? _text;
        private readonly decimal? _number;
        private readonly int _decimals;
        private readonly bool _guarded;

        private CsvCell(string? text,
                        decimal? number,
                        int decimals,
                        bool guarded)
        {
            _text = text;
            _number = number;
            _decimals = decimals;
            _guarded = guarded;
        }

        public static CsvCell Empty => default;

        public static CsvCell Text(string? value) => new(string.IsNullOrWhiteSpace(value) ? null : value.Trim(), null, 0, true);

        public static CsvCell Number(decimal? value,
                                     int decimals = 2) => new(null, value, decimals, false);

        public static CsvCell Count(long? value) => Number(value, 0);

        public static CsvCell Date(DateOnly? value) => new(value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null, 0, false);

        public static CsvCell Time(DateTime? value) => new(value == null
                                                               ? null
                                                               : Csv.Local(value.Value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                                                           null,
                                                           0,
                                                           false);

        public static CsvCell YesNo(bool? value) => new(value == null ? null : value.Value ? "Yes" : "No", null, 0, false);

        public string Format(CsvDialect dialect)
        {
            string value = _number is decimal number
                ? FormatNumber(number, _decimals, dialect)
                : _text ?? "";

            if (_guarded && value.Length > 0 && FormulaStarts.Contains(value[0]))
                value = "'" + value;

            bool quoted = value.IndexOfAny(new[] { dialect.Delimiter, '"', '\r', '\n' }) >= 0 ||
                          _guarded && value.IndexOfAny(new[] { ',', ';', '\t' }) >= 0;

            return quoted
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }

        private static string FormatNumber(decimal number,
                                           int decimals,
                                           CsvDialect dialect)
        {
            decimal rounded = Math.Round(number, decimals, MidpointRounding.AwayFromZero);

            if (rounded == 0) rounded = 0m;

            return rounded.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
                          .Replace(".", dialect.DecimalSeparator);
        }
    }

    public sealed record CsvColumn<T>(string Header,
                                      Func<T, CsvCell> Value);

    public static class Csv
    {
        public static byte[] Write<T>(CsvDialect dialect,
                                      IReadOnlyList<CsvColumn<T>> columns,
                                      IEnumerable<T> rows)
        {
            StringBuilder text = new();

            AppendRow(text, dialect, columns.Select(column => CsvCell.Text(column.Header)));

            foreach (T row in rows)
                AppendRow(text, dialect, columns.Select(column => column.Value(row)));

            byte[] body = Encoding.UTF8.GetBytes(text.ToString());

            return dialect.ByteOrderMark
                ? Encoding.UTF8.GetPreamble().Concat(body).ToArray()
                : body;
        }

        public static DateTime Local(DateTime value)
        {
            return value.Kind == DateTimeKind.Local
                ? value
                : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeZoneInfo.Local);
        }

        private static void AppendRow(StringBuilder text,
                                      CsvDialect dialect,
                                      IEnumerable<CsvCell> cells)
        {
            text.AppendJoin(dialect.Delimiter, cells.Select(cell => cell.Format(dialect)));
            text.Append("\r\n");
        }
    }
}
