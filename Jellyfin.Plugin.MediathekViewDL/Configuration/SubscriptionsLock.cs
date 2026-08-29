using System;

namespace Jellyfin.Plugin.MediathekViewDL.CuriousApe2020Fork.Configuration;

/// <summary>
/// Serializes access to <see cref="PluginConfiguration.Subscriptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// The subscription list is a plain <see cref="System.Collections.Generic.List{T}"/>, but it is
/// mutated from concurrent ASP.NET requests (the subscription editor) while the scheduled task
/// reads it. A structural change landing mid-read can throw or produce a torn snapshot, so every
/// add/remove/replace and every snapshot runs through <see cref="Run{T}"/>.
/// </para>
/// <para>
/// The lock object is deliberately kept private and callers pass in the work to do, rather than
/// the lock being exposed for callers to <c>lock</c> on themselves: a reachable lock object lets
/// unrelated code take the same lock and deadlock us (MT1000).
/// </para>
/// <para>
/// Static because the state it guards is static too - every caller ultimately reaches the same
/// <c>Plugin.Instance.Configuration</c>, and Jellyfin can replace that configuration object
/// wholesale, so a per-instance or per-configuration lock would not actually be mutually exclusive.
/// </para>
/// </remarks>
internal static class SubscriptionsLock
{
    private static readonly object _syncRoot = new();

    /// <summary>
    /// Runs <paramref name="operation"/> with exclusive access to the subscription list.
    /// </summary>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="operation">The work to perform while holding the lock. Keep it short - it must
    /// not await, and must not call back into anything that takes this lock again.</param>
    /// <returns>Whatever <paramref name="operation"/> returned.</returns>
    internal static T Run<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_syncRoot)
        {
            return operation();
        }
    }
}
