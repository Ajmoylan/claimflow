using System.ComponentModel.DataAnnotations;

namespace ClaimFlow.Api.Services;

/// <summary>Safety settings for the public demo. Bound from the "Demo" configuration section.</summary>
public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    /// <summary>Hard cap on claims created by visitors. Seed claims are not counted.</summary>
    [Range(1, 10_000)]
    public int MaxVisitorClaims { get; set; } = 200;

    /// <summary>Run the automatic nightly reset. Turned off in tests, which reset explicitly.</summary>
    public bool AutoResetEnabled { get; set; } = true;

    /// <summary>Local time of day (in <see cref="ResetTimeZone"/>) when the demo data is restored.</summary>
    public TimeOnly ResetTimeOfDay { get; set; } = new(3, 0);

    /// <summary>IANA time zone for the reset schedule.</summary>
    public string ResetTimeZone { get; set; } = "Europe/Oslo";

    /// <summary>How often the background service checks whether a reset is due.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "1.00:00:00")]
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(15);
}
