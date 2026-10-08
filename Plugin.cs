using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.LiveTvCleaner.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LiveTvCleaner;

/// <summary>
/// The main plugin class for Live TV Cleaner.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of IApplicationPaths.</param>
    /// <param name="xmlSerializer">Instance of IXmlSerializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Live TV Cleaner";

    /// <inheritdoc />
    public override string Description => "Utility to clean orphaned Live TV channels, reset channels, and purge stale guide data.";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("a5d6f3e1-8842-4217-bf41-4824e86dbdf9");

    /// <summary>
    /// Gets the current plugin version as a string.
    /// </summary>
    public static string PluginVersion => typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "1.0.5.0";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                DisplayName = "Live TV Cleaner",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace),
                EnableInMainMenu = true,
                MenuSection = "Live TV",
                MenuIcon = "live_tv"
            }
        ];
    }
}
