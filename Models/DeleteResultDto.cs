using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LiveTvCleaner.Models;

/// <summary>
/// Result of a channel or program deletion operation.
/// </summary>
public class DeleteResultDto
{
    /// <summary>
    /// Gets or sets a value indicating whether the operation succeeded.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Gets or sets a descriptive status message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the count of channels deleted.
    /// </summary>
    public int DeletedChannels { get; set; }

    /// <summary>
    /// Gets or sets the count of guide program items deleted.
    /// </summary>
    public int DeletedPrograms { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of when deletion completed.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Request payload for bulk channel deletion.
/// </summary>
public class BulkDeleteRequest
{
    /// <summary>
    /// Gets or sets the list of channel IDs to delete.
    /// </summary>
    public IReadOnlyList<Guid> ChannelIds { get; set; } = [];
}
