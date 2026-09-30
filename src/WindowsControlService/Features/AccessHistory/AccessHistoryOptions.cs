using System.ComponentModel.DataAnnotations;

namespace WindowsControlService.Features.AccessHistory;

public sealed class AccessHistoryOptions
{
    public const string Section = "AccessHistory";

    public TimeSpan IngestionInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How far back each cycle re-reads. There is no watermark: the insert ignores rows it already
    /// has, which is what makes re-reading the whole window safe (see LogonEventRepository).
    /// </summary>
    public TimeSpan IngestionWindow { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Beyond this, a computed session length is treated as nonsense and reported as unknown.
    /// An absurd interval almost always means the real start fell outside the window.
    /// </summary>
    public TimeSpan MaxPlausibleSessionLength { get; set; } = TimeSpan.FromDays(7);

    [Range(1, 500)]
    public int DefaultPageSize { get; set; } = 10;

    [Range(1, 5000)]
    public int MaxPageSize { get; set; } = 500;
}
