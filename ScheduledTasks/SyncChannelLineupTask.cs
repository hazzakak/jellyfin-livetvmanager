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
/// Scheduled task to fast-sync Live TV channel names and numbers from configured tuners without refreshing EPG programs.
/// </summary>
public class SyncChannelLineupTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly ILiveTvCleanerService _cleanerService;
    private readonly ILogger<SyncChannelLineupTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncChannelLineupTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of ILibraryManager.</param>
    /// <param name="config">Instance of IConfigurationManager.</param>
    /// <param name="liveTvManager">Instance of ILiveTvManager.</param>
    /// <param name="taskManager">Instance of ITaskManager.</param>
    /// <param name="loggerFactory">Instance of ILoggerFactory.</param>
    public SyncChannelLineupTask(
        ILibraryManager libraryManager,
        IConfigurationManager config,
        ILiveTvManager liveTvManager,
        ITaskManager taskManager,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<SyncChannelLineupTask>();
        _cleanerService = new LiveTvCleanerService(
            libraryManager,
            config,
            liveTvManager,
            taskManager,
            loggerFactory.CreateLogger<LiveTvCleanerService>());
    }

    /// <inheritdoc />
    public string Name => "Sync Live TV Channel Lineup (No EPG)";

    /// <inheritdoc />
    public string Key => "SyncLiveTvChannelLineup";

    /// <inheritdoc />
    public string Description => "Fast-syncs Live TV channel names and numbers from configured tuners (HDHomeRun/M3U) in under 1 second without running a full XMLTV EPG guide refresh.";

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
        _logger.LogInformation("Starting scheduled task: Sync Live TV Channel Lineup (No EPG)");
        var result = await _cleanerService.SyncChannelLineupAsync(progress, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Finished scheduled task: {Message}", result.Message);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [];
    }
}
