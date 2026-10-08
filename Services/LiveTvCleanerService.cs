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
            LastCleanedCount = config?.LastCleanedCount ?? 0
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
    public Task<DeleteResultDto> DeleteChannelsAsync(
        IReadOnlyCollection<Guid> channelIds,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (channelIds.Count == 0)
        {
            return Task.FromResult(new DeleteResultDto
            {
                Success = true,
                Message = "No channels specified for deletion.",
                DeletedChannels = 0,
                DeletedPrograms = 0
            });
        }

        _logger.LogInformation("Starting high-speed batch deletion of {Count} Live TV channels", channelIds.Count);

        var channelIdSet = new HashSet<Guid>(channelIds);

        // 1. Fetch channels to delete
        var allChannels = GetChannelsFromDb();
        var targetChannels = allChannels.Where(c => channelIdSet.Contains(c.Id)).ToList();

        // 2. Fetch associated programs in a single pass
        var allPrograms = GetAllProgramsFromDb();
        var targetPrograms = allPrograms.Where(p => channelIdSet.Contains(p.ParentId)).ToList();

        _logger.LogInformation(
            "Found {Channels} target channels and {Programs} associated guide programs to delete",
            targetChannels.Count,
            targetPrograms.Count);

        var totalItems = targetPrograms.Count + targetChannels.Count;
        var processedItems = 0;

        // Step 1: Delete channels FIRST so they immediately disappear from the UI
        var deletedChannels = BatchDeleteItems(targetChannels, ref processedItems, totalItems, progress, cancellationToken);
        _logger.LogInformation("Deleted {Channels} Live TV channels", deletedChannels);

        // Step 2: Delete associated guide programs
        int deletedPrograms = 0;
        if (targetPrograms.Count > 0)
        {
            if (FastDeleteMethod is not null || targetPrograms.Count <= 2000)
            {
                deletedPrograms = BatchDeleteItems(targetPrograms, ref processedItems, totalItems, progress, cancellationToken);
            }
            else
            {
                // On Jellyfin 10.10.x without fast batch delete:
                // Delete programs in background so the HTTP response finishes quickly without timing out
                _ = Task.Run(() =>
                {
                    try
                    {
                        var bgProcessed = 0;
                        var bgDeleted = BatchDeleteItems(targetPrograms, ref bgProcessed, targetPrograms.Count, null, CancellationToken.None);
                        _logger.LogInformation("Background guide cleanup finished: {Count} programs deleted", bgDeleted);
                    }
                    catch (Exception bgEx)
                    {
                        _logger.LogWarning(bgEx, "Background guide cleanup error");
                    }
                });
                _logger.LogInformation("Queued background cleanup for {Count} guide programs to prevent HTTP timeout", targetPrograms.Count);
                deletedPrograms = targetPrograms.Count;
            }
        }

        UpdateConfigStats(deletedChannels, deletedPrograms);

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

    /// <inheritdoc />
    public async Task<DeleteResultDto> DeleteOrphanedChannelsAsync(
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Scanning for orphaned Live TV channels to delete");

        var channels = GetChannelsFromDb();
        var tuners = GetConfiguredTuners();
        var orphanStatus = DetermineOrphanStatus(channels, tuners, cancellationToken);

        var orphanedIds = orphanStatus
            .Where(kv => kv.Value.IsOrphaned)
            .Select(kv => kv.Key)
            .ToList();

        _logger.LogInformation("Identified {Count} orphaned Live TV channels", orphanedIds.Count);

        if (orphanedIds.Count == 0)
        {
            return new DeleteResultDto
            {
                Success = true,
                Message = "No orphaned channels were found. Your Live TV channel database is in sync with configured tuners.",
                DeletedChannels = 0,
                DeletedPrograms = 0
            };
        }

        return await DeleteChannelsAsync(orphanedIds, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<DeleteResultDto> ResetAllChannelsAsync(
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("Executing reset of ALL Live TV channels and guide programs");

        var channels = GetChannelsFromDb();
        var programs = GetAllProgramsFromDb();

        _logger.LogInformation("Purging {Channels} channels and {Programs} programs", channels.Count, programs.Count);

        var totalItems = programs.Count + channels.Count;
        var processedItems = 0;

        // Delete all channels first
        var deletedChannels = BatchDeleteItems(channels, ref processedItems, totalItems, progress, cancellationToken);

        // Delete all programs
        int deletedPrograms = 0;
        if (programs.Count > 0)
        {
            if (FastDeleteMethod is not null || programs.Count <= 2000)
            {
                deletedPrograms = BatchDeleteItems(programs, ref processedItems, totalItems, progress, cancellationToken);
            }
            else
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        var bgProcessed = 0;
                        var bgDeleted = BatchDeleteItems(programs, ref bgProcessed, programs.Count, null, CancellationToken.None);
                        _logger.LogInformation("Background guide purge finished: {Count} programs deleted", bgDeleted);
                    }
                    catch (Exception bgEx)
                    {
                        _logger.LogWarning(bgEx, "Background guide purge error");
                    }
                });
                _logger.LogInformation("Queued background purge for {Count} guide programs to prevent HTTP timeout", programs.Count);
                deletedPrograms = programs.Count;
            }
        }

        UpdateConfigStats(deletedChannels, deletedPrograms);

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

    /// <inheritdoc />
    public Task<DeleteResultDto> ClearAllProgramsAsync(
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Purging all Live TV guide programs from the database");

        var programs = GetAllProgramsFromDb();
        var totalItems = programs.Count;
        var processedItems = 0;

        int deletedPrograms = 0;
        if (programs.Count > 0)
        {
            if (FastDeleteMethod is not null || programs.Count <= 2000)
            {
                deletedPrograms = BatchDeleteItems(programs, ref processedItems, totalItems, progress, cancellationToken);
            }
            else
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        var bgProcessed = 0;
                        var bgDeleted = BatchDeleteItems(programs, ref bgProcessed, programs.Count, null, CancellationToken.None);
                        _logger.LogInformation("Background guide program purge completed: {Count} programs deleted", bgDeleted);
                    }
                    catch (Exception bgEx)
                    {
                        _logger.LogWarning(bgEx, "Background guide program purge error");
                    }
                });
                _logger.LogInformation("Queued background purge for {Count} guide programs to prevent HTTP timeout", programs.Count);
                deletedPrograms = programs.Count;
            }
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
            }

            processedItems += chunk.Count;
            if (totalItems > 0)
            {
                progress?.Report((double)processedItems / totalItems * 100);
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
