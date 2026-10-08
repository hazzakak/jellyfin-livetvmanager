using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LiveTvCleaner.Configuration;
using Jellyfin.Plugin.LiveTvCleaner.Models;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvCleaner.Services;

/// <summary>
/// High-performance service for managing and cleaning Live TV channels and guide data.
/// </summary>
public class LiveTvCleanerService : ILiveTvCleanerService
{
    private const int BatchChunkSize = 250;

    private static readonly MethodInfo? FastDeleteMethod = FindFastDeleteMethod();
    private static readonly object SyncLock = new();
    private static CancellationTokenSource? _activeCts;
    private static OperationProgressDto _currentProgress = new() { IsRunning = false, Message = "Idle" };

    private readonly ILibraryManager _libraryManager;
    private readonly IConfigurationManager _config;
    private readonly ILiveTvManager _liveTvManager;
    private readonly ITaskManager _taskManager;
    private readonly ILogger<LiveTvCleanerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveTvCleanerService"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of ILibraryManager.</param>
    /// <param name="config">Instance of IConfigurationManager.</param>
    /// <param name="liveTvManager">Instance of ILiveTvManager.</param>
    /// <param name="taskManager">Instance of ITaskManager.</param>
    /// <param name="logger">Instance of ILogger.</param>
    public LiveTvCleanerService(
        ILibraryManager libraryManager,
        IConfigurationManager config,
        ILiveTvManager liveTvManager,
        ITaskManager taskManager,
        ILogger<LiveTvCleanerService> logger)
    {
        _libraryManager = libraryManager;
        _config = config;
        _liveTvManager = liveTvManager;
        _taskManager = taskManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public OperationProgressDto GetProgress()
    {
        lock (SyncLock)
        {
            return new OperationProgressDto
            {
                IsRunning = _currentProgress.IsRunning,
                OperationName = _currentProgress.OperationName,
                ProcessedItems = _currentProgress.ProcessedItems,
                TotalItems = _currentProgress.TotalItems,
                Percent = _currentProgress.Percent,
                Message = _currentProgress.Message,
                CancellationRequested = _currentProgress.CancellationRequested,
                CanStop = _currentProgress.CanStop
            };
        }
    }

    /// <inheritdoc />
    public bool CancelCurrentOperation()
    {
        lock (SyncLock)
        {
            if (_activeCts is not null && !_activeCts.IsCancellationRequested)
            {
                _logger.LogInformation("Cancellation requested for active operation: {Name}", _currentProgress.OperationName);
                _activeCts.Cancel();
                _currentProgress.CancellationRequested = true;
                _currentProgress.Message = "Cancellation requested. Stopping...";
                return true;
            }

            return false;
        }
    }

    /// <inheritdoc />
    public Task<CleanerStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var channels = GetChannelsFromDb();
        var programCount = GetProgramCount();
        var tuners = GetConfiguredTuners();
        var orphanStatus = DetermineOrphanStatus(channels, tuners, cancellationToken);

        var orphanedCount = orphanStatus.Count(kv => kv.Value.IsOrphaned);
        var activeCount = channels.Count - orphanedCount;

        var config = Plugin.Instance?.Configuration;

        var status = new CleanerStatusDto
        {
            TotalChannels = channels.Count,
            TotalPrograms = programCount,
            OrphanedChannelsCount = orphanedCount,
            ActiveChannelsCount = activeCount,
            ConfiguredTuners = tuners.Select(t => new TunerSummaryDto
            {
                Id = t.Id ?? string.Empty,
                Name = string.IsNullOrWhiteSpace(t.FriendlyName) ? (t.Type ?? "Tuner") : t.FriendlyName,
                Url = t.Url ?? string.Empty,
                Type = t.Type ?? string.Empty
            }).ToList(),
            LastCleanDate = config?.LastCleanDate,
            LastCleanedCount = config?.LastCleanedCount ?? 0,
            PluginVersion = Plugin.PluginVersion
        };

        return Task.FromResult(status);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChannelDto>> GetChannelsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var channels = GetChannelsFromDb();
        var tuners = GetConfiguredTuners();
        var orphanStatus = DetermineOrphanStatus(channels, tuners, cancellationToken);
        var programCounts = GetProgramCountsByChannel();

        var result = new List<ChannelDto>(channels.Count);
        foreach (var channel in channels)
        {
            orphanStatus.TryGetValue(channel.Id, out var statusInfo);
            programCounts.TryGetValue(channel.Id, out var progCount);

            result.Add(new ChannelDto
            {
                Id = channel.Id,
                Name = channel.Name ?? "Unnamed Channel",
                Number = channel.Number ?? string.Empty,
                ChannelType = channel.ChannelType.ToString(),
                ServiceName = channel.ServiceName ?? string.Empty,
                ExternalId = channel.ExternalId ?? string.Empty,
                TunerName = statusInfo?.MatchedTunerName ?? string.Empty,
                IsOrphaned = statusInfo?.IsOrphaned ?? true,
                ProgramCount = progCount
            });
        }

        return Task.FromResult<IReadOnlyList<ChannelDto>>(result.OrderBy(c => c.Number).ThenBy(c => c.Name).ToList());
    }

    /// <inheritdoc />
    public Task<DeleteResultDto> DeleteChannelAsync(Guid channelId, CancellationToken cancellationToken)
    {
        return DeleteChannelsAsync([channelId], null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DeleteResultDto> DeleteChannelsAsync(
        IReadOnlyCollection<Guid> channelIds,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var opName = $"Deleting {channelIds.Count} Channel{(channelIds.Count == 1 ? string.Empty : "s")}";
        var token = BeginOperation(opName, channelIds.Count, cancellationToken);
        try
        {
            return await DeleteChannelsInternalAsync(channelIds, progress, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Channel deletion stopped by user");
            EndOperation("Channel deletion was stopped by user.");
            return new DeleteResultDto
            {
                Success = false,
                Message = "Channel deletion was stopped by user.",
                DeletedChannels = 0,
                DeletedPrograms = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while deleting channels");
            EndOperation($"Error: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<DeleteResultDto> DeleteOrphanedChannelsAsync(
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Scanning for orphaned Live TV channels to delete");
        var token = BeginOperation("Cleaning Orphaned Channels", 0, cancellationToken);
        try
        {
            UpdateProgress(0, 0, "Scanning for orphaned Live TV channels...");
            token.ThrowIfCancellationRequested();

            var channels = GetChannelsFromDb();
            var tuners = GetConfiguredTuners();
            var orphanStatus = DetermineOrphanStatus(channels, tuners, token);

            var orphanedIds = orphanStatus
                .Where(kv => kv.Value.IsOrphaned)
                .Select(kv => kv.Key)
                .ToList();

            _logger.LogInformation("Identified {Count} orphaned Live TV channels", orphanedIds.Count);

            if (orphanedIds.Count == 0)
            {
                EndOperation("No orphaned channels found.");
                return new DeleteResultDto
                {
                    Success = true,
                    Message = "No orphaned channels were found. Your Live TV channel database is in sync with configured tuners.",
                    DeletedChannels = 0,
                    DeletedPrograms = 0
                };
            }

            return await DeleteChannelsInternalAsync(orphanedIds, progress, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Cleaning orphaned channels stopped by user");
            EndOperation("Cleaning orphaned channels was stopped by user.");
            return new DeleteResultDto
            {
                Success = false,
                Message = "Cleaning orphaned channels was stopped by user.",
                DeletedChannels = 0,
                DeletedPrograms = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while cleaning orphaned channels");
            EndOperation($"Error: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc />
    public Task<DeleteResultDto> ResetAllChannelsAsync(
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var token = BeginOperation("Resetting All Live TV Channels", 0, cancellationToken);
        try
        {
            _logger.LogWarning("Executing reset of ALL Live TV channels and guide programs");
            UpdateProgress(0, 0, "Gathering all channels and guide programs...");
            token.ThrowIfCancellationRequested();

            var channels = GetChannelsFromDb();
            var programs = GetAllProgramsFromDb();

            _logger.LogInformation("Purging {Channels} channels and {Programs} programs", channels.Count, programs.Count);

            var totalItems = programs.Count + channels.Count;
            var processedItems = 0;
            UpdateProgress(0, totalItems, $"Purging {channels.Count} channels...");

            // Delete all channels first
            var deletedChannels = BatchDeleteItems(channels, ref processedItems, totalItems, progress, token);

            int deletedPrograms = 0;
            var backgroundTaskStarted = false;

            if (programs.Count > 0)
            {
                if (FastDeleteMethod is not null || programs.Count <= 2000)
                {
                    UpdateProgress(processedItems, totalItems, $"Purging {programs.Count} guide programs...");
                    deletedPrograms = BatchDeleteItems(programs, ref processedItems, totalItems, progress, token);
                }
                else
                {
                    backgroundTaskStarted = true;
                    SwitchOperationPhase("Background Guide Purge", programs.Count, $"Purging {programs.Count} guide programs in background...");
                    var bgToken = _activeCts?.Token ?? CancellationToken.None;

                    _ = Task.Run(() =>
                    {
                        try
                        {
                            var bgProcessed = 0;
                            var bgDeleted = BatchDeleteItems(programs, ref bgProcessed, programs.Count, progress, bgToken);
                            _logger.LogInformation("Background guide purge finished: {Count} programs deleted", bgDeleted);
                            UpdateConfigStats(0, bgDeleted);
                            EndOperation($"Background guide purge completed: {bgDeleted} programs deleted.");
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogWarning("Background guide purge stopped by user");
                            EndOperation("Background guide purge stopped by user.");
                        }
                        catch (Exception bgEx)
                        {
                            _logger.LogWarning(bgEx, "Background guide purge error");
                            EndOperation($"Background guide purge error: {bgEx.Message}");
                        }
                    });

                    _logger.LogInformation("Queued background purge for {Count} guide programs to prevent HTTP timeout", programs.Count);
                    deletedPrograms = programs.Count;
                }
            }

            UpdateConfigStats(deletedChannels, deletedPrograms);

            if (!backgroundTaskStarted)
            {
                EndOperation($"Full Live TV Reset complete. Removed {deletedChannels} channels and {deletedPrograms} guide programs.");
            }

            var result = new DeleteResultDto
            {
                Success = true,
                Message = $"Full Live TV Reset complete. Removed {deletedChannels} channels and {deletedPrograms} guide programs.",
                DeletedChannels = deletedChannels,
                DeletedPrograms = deletedPrograms
            };

            _logger.LogInformation("Full Live TV Reset finished: {Channels} channels, {Programs} programs", deletedChannels, deletedPrograms);
            return Task.FromResult(result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Full Live TV Reset was stopped by user");
            EndOperation("Full Live TV Reset was stopped by user.");
            return Task.FromResult(new DeleteResultDto
            {
                Success = false,
                Message = "Full Live TV Reset was stopped by user.",
                DeletedChannels = 0,
                DeletedPrograms = 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during full Live TV reset");
            EndOperation($"Error: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc />
    public Task<DeleteResultDto> ClearAllProgramsAsync(
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var token = BeginOperation("Purging Guide Programs", 0, cancellationToken);
        try
        {
            _logger.LogInformation("Purging all Live TV guide programs from the database");
            UpdateProgress(0, 0, "Gathering guide programs...");
            token.ThrowIfCancellationRequested();

            var programs = GetAllProgramsFromDb();
            var totalItems = programs.Count;
            var processedItems = 0;
            UpdateProgress(0, totalItems, $"Purging {totalItems} guide programs...");

            int deletedPrograms = 0;
            var backgroundTaskStarted = false;

            if (programs.Count > 0)
            {
                if (FastDeleteMethod is not null || programs.Count <= 2000)
                {
                    deletedPrograms = BatchDeleteItems(programs, ref processedItems, totalItems, progress, token);
                }
                else
                {
                    backgroundTaskStarted = true;
                    SwitchOperationPhase("Background Guide Purge", programs.Count, $"Purging {programs.Count} guide programs in background...");
                    var bgToken = _activeCts?.Token ?? CancellationToken.None;

                    _ = Task.Run(() =>
                    {
                        try
                        {
                            var bgProcessed = 0;
                            var bgDeleted = BatchDeleteItems(programs, ref bgProcessed, programs.Count, progress, bgToken);
                            _logger.LogInformation("Background guide program purge completed: {Count} programs deleted", bgDeleted);
                            UpdateConfigStats(0, bgDeleted);
                            EndOperation($"Background guide program purge completed: {bgDeleted} programs deleted.");
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogWarning("Background guide program purge stopped by user");
                            EndOperation("Background guide program purge stopped by user.");
                        }
                        catch (Exception bgEx)
                        {
                            _logger.LogWarning(bgEx, "Background guide program purge error");
                            EndOperation($"Background guide program purge error: {bgEx.Message}");
                        }
                    });

                    _logger.LogInformation("Queued background purge for {Count} guide programs to prevent HTTP timeout", programs.Count);
                    deletedPrograms = programs.Count;
                }
            }

            if (!backgroundTaskStarted)
            {
                EndOperation($"Successfully purged {deletedPrograms} Live TV guide programs.");
            }

            var result = new DeleteResultDto
            {
                Success = true,
                Message = $"Successfully purged {deletedPrograms} Live TV guide programs.",
                DeletedChannels = 0,
                DeletedPrograms = deletedPrograms
            };

            _logger.LogInformation("Guide purge completed: {Count} programs deleted", deletedPrograms);
            return Task.FromResult(result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Guide purge stopped by user");
            EndOperation("Guide purge was stopped by user.");
            return Task.FromResult(new DeleteResultDto
            {
                Success = false,
                Message = "Guide purge was stopped by user.",
                DeletedChannels = 0,
                DeletedPrograms = 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during guide purge");
            EndOperation($"Error: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc />
    public Task RefreshGuideAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Triggering guide refresh scheduled task");

        var refreshTask = _taskManager.ScheduledTasks
            .FirstOrDefault(t => t.ScheduledTask.Key.Equals("RefreshGuide", StringComparison.OrdinalIgnoreCase)
                              || t.Name.Contains("Refresh Guide", StringComparison.OrdinalIgnoreCase));

        if (refreshTask is not null)
        {
            _taskManager.QueueScheduledTask(refreshTask.ScheduledTask, new TaskOptions());
            _logger.LogInformation("Scheduled task '{Name}' queued", refreshTask.Name);
        }
        else
        {
            _logger.LogWarning("Refresh Guide scheduled task not found on server");
        }

        return Task.CompletedTask;
    }

    private static CancellationToken BeginOperation(string operationName, int totalItems, CancellationToken requestToken)
    {
        lock (SyncLock)
        {
            if (_currentProgress.IsRunning)
            {
                throw new InvalidOperationException($"Operation '{_currentProgress.OperationName}' is already in progress. Please wait or stop it first.");
            }

            _activeCts?.Dispose();
            _activeCts = CancellationTokenSource.CreateLinkedTokenSource(requestToken);

            _currentProgress = new OperationProgressDto
            {
                IsRunning = true,
                OperationName = operationName,
                ProcessedItems = 0,
                TotalItems = totalItems,
                Percent = 0,
                Message = totalItems > 0 ? $"Starting {operationName} (0/{totalItems})..." : $"Starting {operationName}...",
                CancellationRequested = false,
                CanStop = true
            };

            return _activeCts.Token;
        }
    }

    private static void UpdateProgress(int processed, int total, string? customMessage = null)
    {
        lock (SyncLock)
        {
            _currentProgress.ProcessedItems = processed;
            _currentProgress.TotalItems = total;
            var percent = total > 0 ? Math.Clamp((int)Math.Round((double)processed / total * 100), 0, 100) : 0;
            _currentProgress.Percent = percent;
            _currentProgress.Message = customMessage ?? $"Processed {processed:N0} of {total:N0} items ({percent}%)...";
        }
    }

    private static void SwitchOperationPhase(string newOperationName, int newTotal, string initialMessage)
    {
        lock (SyncLock)
        {
            _currentProgress.OperationName = newOperationName;
            _currentProgress.ProcessedItems = 0;
            _currentProgress.TotalItems = newTotal;
            _currentProgress.Percent = 0;
            _currentProgress.Message = initialMessage;
        }
    }

    private static void EndOperation(string completionMessage)
    {
        lock (SyncLock)
        {
            _currentProgress.IsRunning = false;
            _currentProgress.Percent = 100;
            _currentProgress.Message = completionMessage;
            _currentProgress.CancellationRequested = false;
            _activeCts?.Dispose();
            _activeCts = null;
        }
    }

    private Task<DeleteResultDto> DeleteChannelsInternalAsync(
        IReadOnlyCollection<Guid> channelIds,
        IProgress<double>? progress,
        CancellationToken token)
    {
        if (channelIds.Count == 0)
        {
            EndOperation("No channels specified for deletion.");
            return Task.FromResult(new DeleteResultDto
            {
                Success = true,
                Message = "No channels specified for deletion.",
                DeletedChannels = 0,
                DeletedPrograms = 0
            });
        }

        token.ThrowIfCancellationRequested();
        _logger.LogInformation("Starting high-speed batch deletion of {Count} Live TV channels", channelIds.Count);

        var channelIdSet = new HashSet<Guid>(channelIds);

        UpdateProgress(0, channelIds.Count, "Gathering channels and guide programs...");
        var allChannels = GetChannelsFromDb();
        var targetChannels = allChannels.Where(c => channelIdSet.Contains(c.Id)).ToList();

        var allPrograms = GetAllProgramsFromDb();
        var targetPrograms = allPrograms.Where(p => channelIdSet.Contains(p.ParentId)).ToList();

        _logger.LogInformation(
            "Found {Channels} target channels and {Programs} associated guide programs to delete",
            targetChannels.Count,
            targetPrograms.Count);

        var totalItems = targetPrograms.Count + targetChannels.Count;
        var processedItems = 0;

        UpdateProgress(0, totalItems, $"Deleting {targetChannels.Count} channels...");

        // Step 1: Delete channels FIRST so they immediately disappear from the UI
        var deletedChannels = BatchDeleteItems(targetChannels, ref processedItems, totalItems, progress, token);
        _logger.LogInformation("Deleted {Channels} Live TV channels", deletedChannels);

        // Step 2: Delete associated guide programs
        int deletedPrograms = 0;
        var backgroundTaskStarted = false;

        if (targetPrograms.Count > 0)
        {
            if (FastDeleteMethod is not null || targetPrograms.Count <= 2000)
            {
                UpdateProgress(processedItems, totalItems, $"Deleting {targetPrograms.Count} guide programs...");
                deletedPrograms = BatchDeleteItems(targetPrograms, ref processedItems, totalItems, progress, token);
            }
            else
            {
                // Background cleanup on Jellyfin 10.10.x for large sets to avoid HTTP timeout
                backgroundTaskStarted = true;
                SwitchOperationPhase("Background Guide Cleanup", targetPrograms.Count, $"Purging {targetPrograms.Count} guide programs in background...");

                var bgToken = _activeCts?.Token ?? CancellationToken.None;

                _ = Task.Run(() =>
                {
                    try
                    {
                        var bgProcessed = 0;
                        var bgDeleted = BatchDeleteItems(targetPrograms, ref bgProcessed, targetPrograms.Count, progress, bgToken);
                        _logger.LogInformation("Background guide cleanup finished: {Count} programs deleted", bgDeleted);
                        UpdateConfigStats(0, bgDeleted);
                        EndOperation($"Background guide cleanup completed: {bgDeleted} programs deleted.");
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("Background guide cleanup stopped by user");
                        EndOperation("Background guide cleanup stopped by user.");
                    }
                    catch (Exception bgEx)
                    {
                        _logger.LogWarning(bgEx, "Background guide cleanup error");
                        EndOperation($"Background guide cleanup error: {bgEx.Message}");
                    }
                });

                _logger.LogInformation("Queued background cleanup for {Count} guide programs to prevent HTTP timeout", targetPrograms.Count);
                deletedPrograms = targetPrograms.Count;
            }
        }

        UpdateConfigStats(deletedChannels, deletedPrograms);

        if (!backgroundTaskStarted)
        {
            EndOperation($"Deletion complete: Removed {deletedChannels} channels and cleaned {deletedPrograms} guide programs.");
        }

        var result = new DeleteResultDto
        {
            Success = true,
            Message = $"Deletion complete: Removed {deletedChannels} channels and cleaned {deletedPrograms} guide programs.",
            DeletedChannels = deletedChannels,
            DeletedPrograms = deletedPrograms
        };

        _logger.LogInformation("Channel deletion completed: {Channels} channels, {Programs} programs", deletedChannels, deletedPrograms);
        return Task.FromResult(result);
    }

    private static MethodInfo? FindFastDeleteMethod()
    {
        try
        {
            return typeof(ILibraryManager).GetMethods()
                .FirstOrDefault(m => m.Name == "DeleteItemsUnsafeFast");
        }
        catch
        {
            return null;
        }
    }

    private int BatchDeleteItems<T>(
        IReadOnlyList<T> items,
        ref int processedItems,
        int totalItems,
        IProgress<double>? progress,
        CancellationToken cancellationToken) where T : BaseItem
    {
        if (items.Count == 0)
        {
            return 0;
        }

        var deleted = 0;
        var deleteOptions = new DeleteOptions
        {
            DeleteFileLocation = false,
            DeleteFromExternalProvider = false
        };

        for (var i = 0; i < items.Count; i += BatchChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = items.Skip(i).Take(BatchChunkSize).ToList();

            if (FastDeleteMethod is not null)
            {
                try
                {
                    var baseItemChunk = chunk.Cast<BaseItem>().ToList();
                    var parameters = FastDeleteMethod.GetParameters();
                    object[] args = parameters.Length switch
                    {
                        1 => [baseItemChunk],
                        2 => [baseItemChunk, false],
                        _ => null!
                    };

                    if (args != null)
                    {
                        FastDeleteMethod.Invoke(_libraryManager, args);
                        deleted += chunk.Count;
                        processedItems += chunk.Count;
                        UpdateProgress(processedItems, totalItems);
                        if (totalItems > 0)
                        {
                            progress?.Report((double)processedItems / totalItems * 100);
                        }

                        continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Fast bulk delete via reflection failed for batch of {Count} items, falling back to individual delete", chunk.Count);
                }
            }

            // Fallback: Individual item deletion (Jellyfin 10.10.x and older)
            foreach (var item in chunk)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    _libraryManager.DeleteItem(item, deleteOptions, false);
                    deleted++;
                }
                catch (Exception itemEx)
                {
                    _logger.LogError(itemEx, "Failed to delete item {Id} ({Type})", item.Id, item.GetType().Name);
                }

                processedItems++;
                if (processedItems % 10 == 0 || processedItems == totalItems)
                {
                    UpdateProgress(processedItems, totalItems);
                    if (totalItems > 0)
                    {
                        progress?.Report((double)processedItems / totalItems * 100);
                    }
                }
            }
        }

        return deleted;
    }

    private List<LiveTvChannel> GetChannelsFromDb()
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.LiveTvChannel]
        };

        return _libraryManager.GetItemList(query)
            .OfType<LiveTvChannel>()
            .ToList();
    }

    private List<LiveTvProgram> GetAllProgramsFromDb()
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.LiveTvProgram]
        };

        return _libraryManager.GetItemList(query)
            .OfType<LiveTvProgram>()
            .ToList();
    }

    private int GetProgramCount()
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.LiveTvProgram]
        };

        return _libraryManager.GetCount(query);
    }

    private Dictionary<Guid, int> GetProgramCountsByChannel()
    {
        var programs = GetAllProgramsFromDb();
        var dict = new Dictionary<Guid, int>();

        foreach (var prog in programs)
        {
            var parentId = prog.ParentId;
            if (parentId != Guid.Empty)
            {
                dict[parentId] = dict.GetValueOrDefault(parentId, 0) + 1;
            }
        }

        return dict;
    }

    private TunerHostInfo[] GetConfiguredTuners()
    {
        try
        {
            var options = _config.GetConfiguration<LiveTvOptions>("livetv");
            return options?.TunerHosts ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load LiveTV configuration options");
            return [];
        }
    }

    private sealed record ChannelStatusRecord(bool IsOrphaned, string MatchedTunerName);

    private Dictionary<Guid, ChannelStatusRecord> DetermineOrphanStatus(
        IReadOnlyList<LiveTvChannel> channels,
        TunerHostInfo[] tuners,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ChannelStatusRecord>();

        // If no tuners exist, every single channel is orphaned
        if (tuners.Length == 0)
        {
            foreach (var ch in channels)
            {
                result[ch.Id] = new ChannelStatusRecord(true, "No tuners configured");
            }

            return result;
        }

        // Build valid keys from configured tuners
        var tunerKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tuner in tuners)
        {
            var displayName = string.IsNullOrWhiteSpace(tuner.FriendlyName)
                ? (tuner.Type ?? "Tuner")
                : tuner.FriendlyName;

            if (!string.IsNullOrWhiteSpace(tuner.Id))
            {
                tunerKeys[tuner.Id] = displayName;
            }

            if (!string.IsNullOrWhiteSpace(tuner.DeviceId))
            {
                tunerKeys[tuner.DeviceId] = displayName;
            }

            if (!string.IsNullOrWhiteSpace(tuner.Url))
            {
                var hash = ComputeMd5(tuner.Url);
                tunerKeys[hash] = displayName;
                tunerKeys["m3u_" + hash] = displayName;
            }
        }

        // Check each channel
        foreach (var channel in channels)
        {
            var externalId = channel.ExternalId ?? string.Empty;
            var path = channel.Path ?? string.Empty;

            var matchedTuner = string.Empty;
            var isMatched = false;

            foreach (var (key, tunerName) in tunerKeys)
            {
                if ((!string.IsNullOrEmpty(externalId) && externalId.Contains(key, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(path) && path.Contains(key, StringComparison.OrdinalIgnoreCase)))
                {
                    matchedTuner = tunerName;
                    isMatched = true;
                    break;
                }
            }

            result[channel.Id] = new ChannelStatusRecord(!isMatched, matchedTuner);
        }

        return result;
    }

    private static string ComputeMd5(string input)
    {
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private void UpdateConfigStats(int channelsDeleted, int programsDeleted)
    {
        if (Plugin.Instance is null)
        {
            return;
        }

        var config = Plugin.Instance.Configuration;
        config.LastCleanDate = DateTime.UtcNow;
        config.LastCleanedCount = channelsDeleted;
        config.LastCleanedProgramsCount = programsDeleted;
        Plugin.Instance.SaveConfiguration();
    }
}
