using System;

namespace Jellyfin.Plugin.LiveTvCleaner.Models;

/// <summary>
/// Data transfer object representing a Live TV Channel.
/// </summary>
public class ChannelDto
{
    /// <summary>
    /// Gets or sets the internal Jellyfin item ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the channel name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel number.
    /// </summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel type (TV or Radio).
    /// </summary>
    public string ChannelType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the service name that registered the channel.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the external identifier from the provider/tuner.
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets matched tuner name or device name if resolved.
    /// </summary>
    public string TunerName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this channel is orphaned.
    /// </summary>
    public bool IsOrphaned { get; set; }

    /// <summary>
    /// Gets or sets the count of guide program items associated with this channel.
    /// </summary>
    public int ProgramCount { get; set; }
}
