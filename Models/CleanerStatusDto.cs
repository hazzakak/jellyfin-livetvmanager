using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LiveTvCleaner.Models;

/// <summary>
/// Status overview of Live TV channels, programs, and tuners.
/// </summary>
public class CleanerStatusDto
{
    /// <summary>
    /// Gets or sets total Live TV channels found in the database.
    /// </summary>
    public int TotalChannels { get; set; }

    /// <summary>
    /// Gets or sets total Live TV programs (guide entries) found in the database.
    /// </summary>
    public int TotalPrograms { get; set; }

    /// <summary>
    /// Gets or sets count of channels deemed orphaned.
    /// </summary>
    public int OrphanedChannelsCount { get; set; }

    /// <summary>
    /// Gets or sets count of active channels.
    /// </summary>
    public int ActiveChannelsCount { get; set; }

    /// <summary>
    /// Gets or sets list of currently configured tuners.
    /// </summary>
    public IReadOnlyList<TunerSummaryDto> ConfiguredTuners { get; set; } = [];

    /// <summary>
    /// Gets or sets timestamp of the last cleanup.
    /// </summary>
    public DateTime? LastCleanDate { get; set; }

    /// <summary>
    /// Gets or sets the channel count deleted during the last cleanup.
    /// </summary>
    public int LastCleanedCount { get; set; }

    /// <summary>
    /// Gets or sets the version of the currently loaded plugin.
    /// </summary>
    public string PluginVersion { get; set; } = string.Empty;
}

/// <summary>
/// Summary information for a configured tuner device.
/// </summary>
public class TunerSummaryDto
{
    /// <summary>
    /// Gets or sets the tuner identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tuner display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tuner stream or source URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tuner type (e.g. m3u, hdhomerun).
    /// </summary>
    public string Type { get; set; } = string.Empty;
}
