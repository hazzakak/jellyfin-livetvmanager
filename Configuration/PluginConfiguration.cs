using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LiveTvCleaner.Configuration;

/// <summary>
/// Configuration for the Live TV Cleaner plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        AutoCleanOrphanedChannels = false;
        LastCleanDate = null;
        LastCleanedCount = 0;
        LastCleanedProgramsCount = 0;
    }

    /// <summary>
    /// Gets or sets a value indicating whether orphaned channels should be cleaned automatically.
    /// </summary>
    public bool AutoCleanOrphanedChannels { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the last cleanup operation.
    /// </summary>
    public DateTime? LastCleanDate { get; set; }

    /// <summary>
    /// Gets or sets the number of channels deleted in the last cleanup operation.
    /// </summary>
    public int LastCleanedCount { get; set; }

    /// <summary>
    /// Gets or sets the number of programs deleted in the last cleanup operation.
    /// </summary>
    public int LastCleanedProgramsCount { get; set; }
}
