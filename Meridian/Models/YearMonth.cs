namespace Meridian.Models;

public readonly record struct YearMonth(int Year, int Month) : IComparable<YearMonth>
{
    public static YearMonth FromDateTime(DateTime dt) => new(dt.Year, dt.Month);

    public YearMonth Add(int months)
    {
        var dt = new DateTime(Year, Month, 1).AddMonths(months);
        return new(dt.Year, dt.Month);
    }

    public DateTime FirstDay() => new(Year, Month, 1);
    public DateTime FirstDayOfNext() => new DateTime(Year, Month, 1).AddMonths(1);

    public int CompareTo(YearMonth other)
    {
        int y = Year.CompareTo(other.Year);
        return y != 0 ? y : Month.CompareTo(other.Month);
    }

    public override string ToString() => $"{Year:0000}-{Month:00}";
}
