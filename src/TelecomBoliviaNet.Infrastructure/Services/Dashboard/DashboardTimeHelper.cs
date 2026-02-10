namespace TelecomBoliviaNet.Infrastructure.Services.Dashboard;

internal static class DashboardTimeHelper
{
    internal static readonly TimeSpan Bo = TimeSpan.FromHours(-4);

    internal static DateTime NowBo() =>
        DateTimeOffset.UtcNow.ToOffset(Bo).DateTime;

    internal static DateTime StartOfDayUtc(DateTime bo) =>
        new DateTimeOffset(bo.Year, bo.Month, bo.Day, 0, 0, 0, Bo).UtcDateTime;

    internal static DateTime StartOfMonthUtc(DateTime bo) =>
        new DateTimeOffset(bo.Year, bo.Month, 1, 0, 0, 0, Bo).UtcDateTime;

    internal static string UtcToBoHHmm(DateTime utc) =>
        new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(Bo).ToString("HH:mm");

    internal static int UtcHourToBo(int h) => (h - 4 + 24) % 24;

    internal static string Cap(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
