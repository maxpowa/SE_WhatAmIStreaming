using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI;
using Sandbox.Game.Localization;
using Sandbox.Game.Replication;
using Sandbox.Game.World;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Network;
using VRage.Replication;

namespace SE_WhatAmIStreaming;

/// <summary>
/// Tracks grid entities that are created while they stream in, so the wheel
/// text can break the count down by grid category. A grid stays tracked from
/// the moment its constructor runs until its replicable is ready (fired via
/// <c>MyReplicationClient.OnReplicableReady</c>) or a sweep notices its
/// replicable is no longer pending (e.g. locally created grids).
/// </summary>
internal static class StreamingGridTracker
{
    private static readonly ConcurrentDictionary<MyCubeGrid, byte> s_loadingGrids = new();
    private static MyReplicationClient? s_client;

    public static int Count => s_loadingGrids.Count;

    public static IEnumerable<MyCubeGrid> Active => s_loadingGrids.Keys;

    /// <summary>
    /// Called from the <c>MyCubeGrid</c> constructor postfix. Only client
    /// sessions track grids; singleplayer and server sessions are ignored.
    /// </summary>
    public static void Track(MyCubeGrid grid)
    {
        if (MyMultiplayer.ReplicationLayer is not MyReplicationClient client)
        {
            return;
        }

        EnsureSubscribed(client);
        s_loadingGrids.TryAdd(grid, 0);
    }

    /// <summary>
    /// Drops tracked grids whose replicable is already hooked but no longer
    /// pending-streaming, e.g. locally created grids that never stream in.
    /// Grids that are still mid-init have no hooked replicable yet and are kept.
    /// </summary>
    public static void Sweep(HashSet<IMyReplicable> pendingStreaming)
    {
        foreach (MyCubeGrid grid in s_loadingGrids.Keys)
        {
            if (MyExternalReplicable.FindByObject(grid) is { } replicable && !pendingStreaming.Contains(replicable))
            {
                s_loadingGrids.TryRemove(grid, out _);
            }
        }
    }

    /// <summary>
    /// Clears the tracked set and any event subscription; called when
    /// streaming stops so stale entries never leak into the next session.
    /// </summary>
    public static void Reset()
    {
        if (s_client != null)
        {
            s_client.OnReplicableReady -= OnReplicableReady;
            s_client = null;
        }

        s_loadingGrids.Clear();
    }

    private static void EnsureSubscribed(MyReplicationClient client)
    {
        if (ReferenceEquals(s_client, client))
        {
            return;
        }

        if (s_client != null)
        {
            s_client.OnReplicableReady -= OnReplicableReady;
        }

        s_client = client;
        client.OnReplicableReady += OnReplicableReady;
    }

    private static void OnReplicableReady(IMyReplicable replicable)
    {
        if (replicable is MyCubeGridReplicable gridReplicable && gridReplicable.Instance is MyCubeGrid grid)
        {
            s_loadingGrids.TryRemove(grid, out _);
        }
    }
}

/// <summary>
/// Replaces the vague "Streaming" rotating-wheel text with the localized
/// text plus a live per-category breakdown of what is streaming in, e.g.
/// "Streaming (2x grid, 1x voxel, 1x small grid, 1x static grid)".
/// </summary>
[HarmonyPatch(typeof(MySession))]
[HarmonyPatch(nameof(MySession.StreamingInProgress), MethodType.Setter)]
internal static class StreamingWheelPatch
{
    private static string? _lastText;
    private static FieldInfo? s_pendingReplicablesField;

    /// <summary>
    /// The setter is invoked every frame by <c>MySession</c>; the body only
    /// acts on transitions, but this postfix runs regardless. We read the
    /// current state and refresh the wheel text while streaming is in progress.
    /// </summary>
    [HarmonyPostfix]
    private static void Postfix(MySession __instance)
    {
        if (!__instance.StreamingInProgress)
        {
            // Streaming stopped; the original setter already cleared the wheel
            // text. Reset our caches so the next run refreshes the text.
            StreamingGridTracker.Reset();
            _lastText = null;
            return;
        }

        if (MyMultiplayer.ReplicationLayer is not MyReplicationClient client)
        {
            return;
        }

        CachingDictionary<NetworkId, MyPendingReplicable> pending = GetPendingReplicables(client);
        if (pending is null)
        {
            return;
        }

        int gridsInFlight = 0;
        int voxelsInFlight = 0;
        HashSet<IMyReplicable> pendingStreaming = new();

        foreach (KeyValuePair<NetworkId, MyPendingReplicable> entry in pending)
        {
            MyPendingReplicable pendingReplicable = entry.Value;
            if (pendingReplicable is null || !pendingReplicable.IsStreaming)
            {
                continue;
            }

            IMyReplicable replicable = pendingReplicable.Replicable;
            if (replicable is null)
            {
                continue;
            }

            pendingStreaming.Add(replicable);

            if (replicable is MyCubeGridReplicable)
            {
                gridsInFlight++;
            }
            else if (replicable.GetType().Name.Contains("Voxel", StringComparison.Ordinal))
            {
                // MyVoxelReplicable is internal, so classify by type name.
                voxelsInFlight++;
            }
        }

        StreamingGridTracker.Sweep(pendingStreaming);

        // Grids that are still streaming but whose entity has not been
        // constructed yet (data still in transit) have no size to show;
        // count them without a category.
        int unclassifiedGrids = Math.Max(0, gridsInFlight - StreamingGridTracker.Count);

        int smallGrids = 0;
        int largeGrids = 0;
        int staticGrids = 0;
        foreach (MyCubeGrid grid in StreamingGridTracker.Active)
        {
            // IsStatic and GridSizeEnum are finalized during entity init,
            // which completes before the replicable becomes ready.
            if (grid.IsStatic)
            {
                staticGrids++;
            }
            else if (grid.GridSizeEnum == MyCubeSize.Small)
            {
                smallGrids++;
            }
            else
            {
                largeGrids++;
            }
        }

        string text = BuildText(unclassifiedGrids, voxelsInFlight, smallGrids, staticGrids, largeGrids);

        if (text == _lastText)
        {
            return;
        }

        // Assign a fresh StringBuilder instance: the HUD label compares
        // references to decide whether to redraw, so mutating in place would
        // not be picked up.
        MyHud.RotatingWheelText = new StringBuilder(text);
        _lastText = text;
    }

    private static string BuildText(int unclassifiedGrids, int voxels, int small, int staticGrids, int large)
    {
        string baseText = MyTexts.GetString(MySpaceTexts.LoadingWheel_Streaming);
        StringBuilder parts = new();
        AppendPart(parts, unclassifiedGrids, "grid");
        AppendPart(parts, voxels, "voxel");
        AppendPart(parts, small, "small grid");
        AppendPart(parts, staticGrids, "static grid");
        AppendPart(parts, large, "large grid");

        return parts.Length == 0
            ? baseText
            : string.Concat(baseText, " (", parts, ")");
    }

    private static void AppendPart(StringBuilder sb, int count, string label)
    {
        if (count <= 0)
        {
            return;
        }

        if (sb.Length > 0)
        {
            sb.Append(", ");
        }

        sb.Append(count).Append("x ").Append(label);
    }

    private static CachingDictionary<NetworkId, MyPendingReplicable>? GetPendingReplicables(MyReplicationClient client)
    {
        if (s_pendingReplicablesField is null)
        {
            s_pendingReplicablesField = typeof(MyReplicationClient).GetField(
                "m_pendingReplicables", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        return s_pendingReplicablesField?.GetValue(client) as CachingDictionary<NetworkId, MyPendingReplicable>;
    }
}

/// <summary>
/// Records newly created cube grids so the wheel can show their size category
/// while they finish loading. The <c>MyCubeGrid(MyCubeSize)</c> constructor
/// is the one every creation path funnels through; the public parameterless
/// constructor chains into it, so patching it covers all of them.
/// </summary>
[HarmonyPatch(typeof(MyCubeGrid), MethodType.Constructor, new[] { typeof(MyCubeSize) })]
internal static class StreamingGridCtorPatch
{
    [HarmonyPostfix]
    private static void Postfix(MyCubeGrid __instance)
    {
        StreamingGridTracker.Track(__instance);
    }
}
