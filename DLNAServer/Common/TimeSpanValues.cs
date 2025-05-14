namespace DLNAServer.Common
{
    /// <summary>
    /// <see cref="TimeSpan"/> values for a decrease memory allocations
    /// </summary>
    public static class TimeSpanValues
    {
        public static readonly TimeSpan TimeDays7 = TimeSpan.FromDays(7);
        public static readonly TimeSpan TimeDays1 = TimeSpan.FromDays(1);
        public static readonly TimeSpan TimeHours12 = TimeSpan.FromHours(12);
        public static readonly TimeSpan TimeHours1 = TimeSpan.FromHours(1);
        public static readonly TimeSpan TimeMin40 = TimeSpan.FromMinutes(40);
        public static readonly TimeSpan TimeMin30 = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan TimeMin20 = TimeSpan.FromMinutes(20);
        public static readonly TimeSpan TimeMin15 = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan TimeMin10 = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan TimeMin5 = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan TimeMin2 = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan TimeMin1 = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan TimeSecs30 = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan TimeSecs10 = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan TimeSecs5 = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan TimeSecs2 = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan TimeSecs1 = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan TimeMs500 = TimeSpan.FromSeconds(0.5);
        public static readonly TimeSpan TimeMs100 = TimeSpan.FromSeconds(0.1);
        public static readonly TimeSpan TimeMs50 = TimeSpan.FromMilliseconds(50);
        public static readonly TimeSpan TimeMs5 = TimeSpan.FromMilliseconds(5);
    }
}