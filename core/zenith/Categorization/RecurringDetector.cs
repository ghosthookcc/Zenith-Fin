namespace ZenithFin.Categorization
{
    public sealed record RecurringSeries(string Frequency,
                                         string Label,
                                         int Occurrences,
                                         DateOnly Last,
                                         DateOnly Next,
                                         bool Manual);

    public static class RecurringDetector
    {
        private const int MergeDays = 3;
        private const int MinimumOccurrences = 3;
        private const double MinimumConsistency = 0.75;

        private sealed record Band(string Frequency,
                                   string Label,
                                   int Low,
                                   int High,
                                   int Days,
                                   int Months);

        private static readonly Band[] Bands =
        {
            new("WEEKLY", "Weekly", 5, 9, 7, 0),
            new("BIWEEKLY", "Every 2 weeks", 12, 17, 14, 0),
            new("MONTHLY", "Monthly", 25, 35, 0, 1),
            new("BIMONTHLY", "Every 2 months", 55, 68, 0, 2),
            new("QUARTERLY", "Every 3 months", 82, 100, 0, 3),
            new("HALF_YEARLY", "Every 6 months", 170, 196, 0, 6),
            new("YEARLY", "Yearly", 350, 380, 0, 12)
        };

        private static readonly Band Monthly = Bands[2];

        public static RecurringSeries? Detect(IEnumerable<DateOnly> dates,
                                              bool? manual)
        {
            if (manual == false) return null;

            DateOnly[] occurrences = Occurrences(dates);

            if (occurrences.Length == 0) return null;

            int[] gaps = occurrences.Zip(occurrences.Skip(1), (earlier, later) => later.DayNumber - earlier.DayNumber).ToArray();
            double? median = gaps.Length == 0 ? null : Median(gaps);
            Band? band = median == null ? null : Bands.FirstOrDefault(candidate => median >= candidate.Low && median <= candidate.High);

            if (manual == true)
                return Series(band ?? (median == null ? Monthly : Nearest(median.Value)), occurrences, true);

            if (occurrences.Length < MinimumOccurrences || band == null) return null;

            double consistency = gaps.Count(gap => gap >= band.Low && gap <= band.High) / (double)gaps.Length;

            return consistency >= MinimumConsistency ? Series(band, occurrences, false) : null;
        }

        private static DateOnly[] Occurrences(IEnumerable<DateOnly> dates)
        {
            List<DateOnly> occurrences = new();

            foreach (DateOnly date in dates.Distinct().OrderBy(date => date))
                if (occurrences.Count == 0 || date.DayNumber - occurrences[^1].DayNumber > MergeDays) occurrences.Add(date);

            return occurrences.ToArray();
        }

        private static double Median(int[] values)
        {
            int[] sorted = values.OrderBy(value => value).ToArray();
            int middle = sorted.Length / 2;

            return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
        }

        private static Band Nearest(double median)
        {
            return Bands.MinBy(band => Math.Abs((band.Low + band.High) / 2.0 - median))!;
        }

        private static RecurringSeries Series(Band band,
                                              DateOnly[] occurrences,
                                              bool manual)
        {
            DateOnly last = occurrences[^1];
            DateOnly next = band.Months > 0 ? last.AddMonths(band.Months) : last.AddDays(band.Days);

            return new RecurringSeries(band.Frequency, band.Label, occurrences.Length, last, next, manual);
        }
    }
}
