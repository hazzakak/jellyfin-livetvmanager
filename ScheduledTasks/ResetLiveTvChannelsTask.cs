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
/// Scheduled task to completely purge all Live TV channels and guide programs.
/// </summary>
public class ResetLiveTvChannelsTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly ILiveTvCleanerService _cleanerService;
    private readonly ILogger<ResetLiveTvChannelsTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResetLiveTvChannelsTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of ILibraryManager.</param>
    /// <param name="config">Instance of IConfigurationManager.</param>
    /// <param name="liveTvManager">Instance of ILiveTvManager.</param>
    /// <param name="taskManager">Instance of ITaskManager.</param>
    /// <param name="loggerFactory">Instance of ILoggerFactory.</param>
    public ResetLiveTvChannelsTask(
        ILibraryManager libraryManager,
        IConfigurationManager config,
        ILiveTvManager liveTvManager,
        ITaskManager taskManager,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ResetLiveTvChannelsTask>();
        _cleanerService = new LiveTvCleanerService(
            libraryManager,
            config,
            liveTvManager,
            taskManager,
            loggerFactory.CreateLogger<LiveTvCleanerService>());
    }

    /// <inheritdoc />
    public string Name => "Reset All Live TV Channels";

    /// <inheritdoc />
    public string Key => "ResetAllLiveTvChannels";

    /// <inheritdoc />
    public string Description => "Completely removes all Live TV channels and guide programs from the database to allow a fresh resync.";

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
        _logger.LogWarning("Starting scheduled task: Reset All Live TV Channels");
        var result = await _cleanerService.ResetAllChannelsAsync(progress, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Finished scheduled task: {Message}", result.Message);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Only run manually by admin
        return [];
    }
}
