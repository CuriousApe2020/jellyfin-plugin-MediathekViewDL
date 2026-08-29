namespace Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration;

/// <summary>
/// Guards structural access to <see cref="PluginConfiguration.Subscriptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// The subscription list is a plain <see cref="System.Collections.Generic.List{T}"/>, but it is
/// mutated from concurrent ASP.NET requests (the subscription editor) while the scheduled task
/// reads it. A structural change landing mid-read can throw or produce a torn snapshot, so every
/// add/remove/replace and every snapshot takes this lock.
/// </para>
/// <para>
/// Deliberately static rather than an injected dependency: the state it guards is itself
/// process-global (<c>Plugin.Instance.Configuration</c>), and Jellyfin can replace that
/// configuration object wholesale, so a per-instance or per-configuration lock would not actually
/// be mutually exclusive between callers.
/// </para>
/// <para>
/// Deliberately <c>internal</c>: a lock object reachable from outside the assembly lets unrelated
/// code take the same lock and deadlock us (MT1000), and nothing outside this plugin has any
/// business touching the subscription list anyway.
/// </para>
/// </remarks>
internal static class SubscriptionsLock
{
    /// <summary>
    /// Gets the object to lock on when modifying or snapshotting the subscription list.
    /// </summary>
    internal static object SyncRoot { get; } = new();
}
