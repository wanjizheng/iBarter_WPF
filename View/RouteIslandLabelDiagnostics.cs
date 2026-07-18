using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace iBarter.View;

/// <summary>
/// Audit round 5: minimal diagnostic logger for the route island
/// label pipeline.  Off by default in release builds; flip
/// <see cref="Enabled"/> on at runtime to confirm whether the
/// regression is "first-call host size is 0" (Deferred) or
/// something deeper (missing island metadata, hd-coordinate gap).
/// </summary>
internal static class RouteIslandLabelDiagnostics {
    /// <summary>
    /// Default false.  Production code never sets this; tests or
    /// a temporary support session can flip it on via the
    /// Immediate window.
    /// </summary>
    public static bool Enabled;

    private static readonly Dictionary<string, int> MissingCounts = new(StringComparer.Ordinal);
    private static DateTime _lastMissingLogUtc = DateTime.MinValue;

    public static void Log(string message) {
        if (!Enabled) return;
        Debug.WriteLine("[RouteIslandLabel] " + message);
    }

    /// <summary>
    /// Records a missing-island-id event.  We do NOT spam the log
    /// for the same island every refresh: each id is counted and
    /// reported once per 5 seconds.
    /// </summary>
    public static void LogMissing(string islandId) {
        if (!Enabled) return;
        MissingCounts.TryGetValue(islandId, out var count);
        MissingCounts[islandId] = count + 1;
        var now = DateTime.UtcNow;
        if ((now - _lastMissingLogUtc).TotalSeconds < 5) return;
        _lastMissingLogUtc = now;
        Debug.WriteLine($"[RouteIslandLabel] missing: {islandId} x{MissingCounts[islandId]}");
    }

    /// <summary>
    /// Test hook: reset accumulated missing counts and the throttle.
    /// </summary>
    public static void ResetForTest() {
        MissingCounts.Clear();
        _lastMissingLogUtc = DateTime.MinValue;
        Enabled = false;
    }
}