using System;

namespace Jellyfin.Plugin.LiveTvCleaner.Models;

/// <summary>
/// DTO representing real-time progress for Live TV cleanup and reset operations.
/// </summary>
public class OperationProgressDto
{
    /// <summary>
    /// Gets or sets a value indicating whether an operation is currently active.
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// Gets or sets the name or description of the current operation.
    /// </summary>
    public string OperationName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of items processed so far.
    /// </summary>
    public int ProcessedItems { get; set; }

    /// <summary>
    /// Gets or sets the total number of items to process.
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Gets or sets the completion percentage (0 - 100).
    /// </summary>
    public int Percent { get; set; }

    /// <summary>
    /// Gets or sets the current progress or status message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether cancellation has been requested.
    /// </summary>
    public bool CancellationRequested { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this operation can be cancelled.
    /// </summary>
    public bool CanStop { get; set; } = true;
}
