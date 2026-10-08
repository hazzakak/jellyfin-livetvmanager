using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LiveTvCleaner.Models;

namespace Jellyfin.Plugin.LiveTvCleaner.Services;

/// <summary>
/// Interface for Live TV channel and guide data cleanup operations.
/// </summary>
public interface ILiveTvCleanerService
{
    /// <summary>
    /// Gets the current status overview of Live TV channels, programs, and tuners.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Status DTO.</returns>
    Task<CleanerStatusDto> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all Live TV channels with their orphan status.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>List of channel DTOs.</returns>
    Task<IReadOnlyList<ChannelDto>> GetChannelsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a specific channel and its associated guide programs.
    /// </summary>
    /// <param name="channelId">Channel item ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Deletion result.</returns>
    Task<DeleteResultDto> DeleteChannelAsync(Guid channelId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes multiple specific channels and their associated guide programs.
    /// </summary>
    /// <param name="channelIds">Collection of channel IDs.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Deletion result.</returns>
    Task<DeleteResultDto> DeleteChannelsAsync(IReadOnlyCollection<Guid> channelIds, IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes all channels that no longer match any active configured tuner device.
    /// </summary>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Deletion result.</returns>
    Task<DeleteResultDto> DeleteOrphanedChannelsAsync(IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes all Live TV channels and all guide programs from the database.
    /// </summary>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Deletion result.</returns>
    Task<DeleteResultDto> ResetAllChannelsAsync(IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes all Live TV guide programs (EPG entries) from the database without deleting channels.
    /// </summary>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Deletion result.</returns>
    Task<DeleteResultDto> ClearAllProgramsAsync(IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Triggers a guide and channels refresh via the server's task manager.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A completed task.</returns>
    Task RefreshGuideAsync(CancellationToken cancellationToken);
}
