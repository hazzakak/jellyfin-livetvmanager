using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LiveTvCleaner.Models;
using Jellyfin.Plugin.LiveTvCleaner.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvCleaner.Controllers;

/// <summary>
/// API Controller for Live TV cleanup operations.
/// Accessible only by Jellyfin server administrators.
/// </summary>
[ApiController]
[Route("LiveTvCleaner")]
[Authorize(Policy = "RequiresElevation")]
[Produces(MediaTypeNames.Application.Json)]
public class LiveTvCleanerController : ControllerBase
{
    private readonly ILiveTvCleanerService _cleanerService;
    private readonly ILogger<LiveTvCleanerController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveTvCleanerController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of ILibraryManager.</param>
    /// <param name="config">Instance of IConfigurationManager.</param>
    /// <param name="liveTvManager">Instance of ILiveTvManager.</param>
    /// <param name="taskManager">Instance of ITaskManager.</param>
    /// <param name="loggerFactory">Instance of ILoggerFactory.</param>
    public LiveTvCleanerController(
        ILibraryManager libraryManager,
        IConfigurationManager config,
        ILiveTvManager liveTvManager,
        ITaskManager taskManager,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<LiveTvCleanerController>();
        _cleanerService = new LiveTvCleanerService(
            libraryManager,
            config,
            liveTvManager,
            taskManager,
            loggerFactory.CreateLogger<LiveTvCleanerService>());
    }

    /// <summary>
    /// Gets the current status of channels, programs, and tuners.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status overview.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CleanerStatusDto>> GetStatus(CancellationToken cancellationToken)
    {
        var status = await _cleanerService.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return Ok(status);
    }

    /// <summary>
    /// Gets all Live TV channels with their orphan status.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of channels.</returns>
    [HttpGet("Channels")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChannelDto>>> GetChannels(CancellationToken cancellationToken)
    {
        var channels = await _cleanerService.GetChannelsAsync(cancellationToken).ConfigureAwait(false);
        return Ok(channels);
    }

    /// <summary>
    /// Force deletes a single channel by ID, bypassing standard delete constraints.
    /// </summary>
    /// <param name="id">Channel item ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Delete result.</returns>
    [HttpDelete("Channels/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<DeleteResultDto>> DeleteChannel(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Admin requested deletion of channel {Id}", id);
        var result = await _cleanerService.DeleteChannelAsync(id, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Deletes a selected list of channels.
    /// </summary>
    /// <param name="request">Payload containing channel IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Delete result.</returns>
    [HttpPost("DeleteSelected")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<DeleteResultDto>> DeleteSelected(
        [FromBody] BulkDeleteRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Admin requested bulk deletion of {Count} channels", request.ChannelIds.Count);
        var result = await _cleanerService.DeleteChannelsAsync(request.ChannelIds, null, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Deletes all orphaned Live TV channels that do not map to any active tuner device.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Delete result.</returns>
    [HttpPost("DeleteOrphaned")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<DeleteResultDto>> DeleteOrphaned(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Admin requested deletion of all orphaned Live TV channels");
        var result = await _cleanerService.DeleteOrphanedChannelsAsync(null, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Completely purges all Live TV channels and guide programs for a clean reset.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Delete result.</returns>
    [HttpPost("DeleteAllChannels")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<DeleteResultDto>> DeleteAllChannels(CancellationToken cancellationToken)
    {
        _logger.LogWarning("Admin requested FULL RESET of all Live TV channels and guide programs");
        var result = await _cleanerService.ResetAllChannelsAsync(null, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Clears only the Live TV guide programs (EPG entries) from the database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Delete result.</returns>
    [HttpPost("DeleteAllPrograms")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<DeleteResultDto>> DeleteAllPrograms(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Admin requested purge of all Live TV guide programs");
        var result = await _cleanerService.ClearAllProgramsAsync(null, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Triggers the server's Refresh Guide scheduled task.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    [HttpPost("RefreshGuide")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> RefreshGuide(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Admin requested guide refresh");
        await _cleanerService.RefreshGuideAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }
}
