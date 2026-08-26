using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Xml.Serialization;
using Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.Groups;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
/// <remarks>
/// <see cref="XmlRootAttribute"/> is set explicitly (rather than relying on the default, which
/// would be the bare class name "PluginConfiguration") because .NET's XmlSerializer generates and
/// caches its (de)serialization code by root element name. The upstream plugin
/// (CatNoir2006/jellyfin-plugin-MediathekViewDL) declares its own, identically-named
/// "Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration.PluginConfiguration" type (this fork intentionally
/// keeps the same C# namespace/class names - see ServiceRegistrator.cs for why). With the default
/// root name, loading both plugins into the same Jellyfin process causes XmlSerializer's internal
/// cache to confuse the two types, throwing InvalidCastException ("[A]...PluginConfiguration cannot
/// be cast to [B]...PluginConfiguration") whenever either plugin's configuration is read or saved.
/// Giving this fork's type a distinct root element name avoids the collision.
/// </remarks>
[XmlRoot(ElementName = "MediathekViewDLForkPluginConfiguration")]
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        Subscriptions = new Collection<Subscription>();
    }

    /// <summary>
    /// Gets or sets the version of the configuration.
    /// Used for migrations.
    /// </summary>
    public int ConfigVersion { get; set; }

    /// <summary>
    /// Gets or sets the configuration paths.
    /// Contains the paths for the different download types.
    /// </summary>
    public ConfigurationPaths Paths { get; set; } = new();

    /// <summary>
    /// Gets or sets the download options.
    /// </summary>
    public DownloadOptions Download { get; set; } = new();

    /// <summary>
    /// Gets or sets the search options.
    /// </summary>
    public SearchOptions Search { get; set; } = new();

    /// <summary>
    /// Gets or sets the network options.
    /// </summary>
    public NetworkOptions Network { get; set; } = new();

    /// <summary>
    /// Gets or sets the maintenance options.
    /// </summary>
    public MaintenanceOptions Maintenance { get; set; } = new();

    /// <summary>
    /// Gets or sets the subscription default values.
    /// </summary>
    public SubscriptionDefaults SubscriptionDefaults { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the setup wizard has been completed.
    /// Used to auto-show the first-run wizard on fresh installations
    /// and to suppress it once the user has finished (or explicitly skipped) the setup.
    /// </summary>
    public bool WizardCompleted { get; set; }

    /// <summary>
    /// Gets the list of download subscriptions.
    /// </summary>
    /// <remarks>
    /// <see cref="XmlArrayItemAttribute"/> pins the per-item XML element tag to "Subscription" -
    /// the item element name that XmlSerializer defaulted to before <see cref="Subscription"/> got
    /// its own explicit, collision-avoiding <see cref="XmlTypeAttribute"/> (which would otherwise
    /// change the default item tag and make existing saved subscriptions unreadable).
    /// </remarks>
    [XmlArrayItem(ElementName = "Subscription")]
    public Collection<Subscription> Subscriptions { get; init; }

    /// <summary>
    /// Gets or sets the timestamp of the last job run.
    /// </summary>
    public DateTime LastRun { get; set; }

    /// <summary>
    /// Gets the list of allowed download domains.
    /// This covers the known CDNs used by ARD and ZDF.
    /// The list does only contain top-level domains subdomains may be added at some point.
    /// </summary>
    public HashSet<string> AllowedDomains => new(StringComparer.OrdinalIgnoreCase)
    {
        "akamaihd.net",
        "akamaized.net",
        "apa.at",
        "ard-mcdn.de",
        "ard.de",
        "ardmediathek.de",
        "br.de",
        "daserste.de",
        "orf.at",
        "srf.ch",
        "zdf.de",
        "kika.de",
    };
}
