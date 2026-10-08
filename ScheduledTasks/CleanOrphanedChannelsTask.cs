using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LiveTvCleaner.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvCleaner.ScheduledTasks;

/// <summary>
/// Scheduled task to scan and clean orphaned Live TV channels.
/// </summary>
public class CleanOrphanedChannelsTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly ILiveTvCleanerService _cleanerService;
    private readonly ILogger<CleanOrphanedChannelsTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CleanOrphanedChannelsTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of ILibraryManager.</param>
    /// <param name="config">Instance of IConfigurationManager.</param>
    /// <param name="liveTvManager">Instance of ILiveTvManager.</param>
    /// <param name="taskManager">Instance of ITaskManager.</param>
    /// <param name="loggerFactory">Instance of ILoggerFactory.</param>
    public CleanOrphanedChannelsTask(
        ILibraryManager libraryManager,
        IConfigurationManager config,
        ILiveTvManager liveTvManager,
        ITaskManager taskManager,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<CleanOrphanedChannelsTask>();
        _cleanerService = new LiveTvCleanerService(
            libraryManager,
            config,
            liveTvManager,
            taskManager,
            loggerFactory.CreateLogger<LiveTvCleanerService>());
    }

    /// <inheritdoc />
    public string Name => "Clean Orphaned Live TV Channels";

    /// <inheritdoc />
    public string Key => "CleanOrphanedLiveTvChannels";

    /// <inheritdoc />
    public string Description => "Scans and removes Live TV channels and guide data that no longer belong to an active tuner device.";

    /// <inheritdoc />
    public string Category => "Live TV";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting scheduled task: Clean Orphaned Live TV Channels");
        var result = await _cleanerService.DeleteOrphanedChannelsAsync(progress, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Finished scheduled task: {Message}", result.Message);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // By default, trigger manually, or users can set a schedule in Dashboard -> Scheduled Tasks
        return [];
    }
}
