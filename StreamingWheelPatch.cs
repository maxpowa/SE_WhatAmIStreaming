using System.Text;
using HarmonyLib;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI;
using Sandbox.Game.Localization;
using Sandbox.Game.World;
using VRage;
using VRage.Network;

namespace SE_WhatAmIStreaming;

/// <summary>
/// Replaces the vague "Streaming" rotating-wheel text with the localized text
/// plus the live count of replicables that are still streaming in, e.g.
/// "Streaming (42)".
/// </summary>
[HarmonyPatch(typeof(MySession))]
[HarmonyPatch(nameof(MySession.StreamingInProgress), MethodType.Setter)]
internal static class StreamingWheelPatch
{
    private static string _lastText;

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
            // text. Reset our cache so the next session refreshes the text.
            _lastText = null;
            return;
        }

        string baseText = MyTexts.GetString(MySpaceTexts.LoadingWheel_Streaming);
        int pendingCount = GetPendingStreamingCount();
        string text = pendingCount > 0 ? string.Concat(baseText, " (", pendingCount, ")") : baseText;

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

    private static int GetPendingStreamingCount()
    {
        if (MyMultiplayer.ReplicationLayer is MyReplicationClient client)
        {
            return client.PendingStreamingRelicablesCount;
        }

        return 0;
    }
}
