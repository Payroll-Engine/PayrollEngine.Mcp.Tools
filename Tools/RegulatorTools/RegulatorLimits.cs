using System;

namespace PayrollEngine.Mcp.Tools.RegulatorTools;

/// <summary>Limits per call of the Regulator read tools.
/// A call processes at most the given number of manifests; the response reports a truncated scope.</summary>
public static class RegulatorLimits
{
    /// <summary>Maximum manifests per call: list of manifests</summary>
    public const int ListManifests = 200;

    /// <summary>Maximum manifests per call: snapshot verification and manifest validation</summary>
    public const int FileChecks = 100;

    /// <summary>Maximum manifests per call: check in development mode</summary>
    public const int Check = 50;

    /// <summary>Maximum manifests per call: check in release mode (verifies the local evidence files)</summary>
    public const int CheckRelease = 25;

    /// <summary>Maximum manifests per call: check with URL requests</summary>
    public const int CheckUrls = 10;

    /// <summary>Maximum manifests per call: source verification against the local evidence</summary>
    public const int VerifySourceLocal = 25;

    /// <summary>Maximum manifests per call: source verification against the live sources</summary>
    public const int VerifySourceLive = 5;

    /// <summary>Maximum number of entries returned per manifest by the source verification</summary>
    public const int MaxEntriesPerManifest = 200;

    /// <summary>Maximum number of snapshot differences returned per manifest</summary>
    public const int MaxDifferences = 200;

    /// <summary>Size of a response in characters above which a tool adds a hint to narrow the output.
    /// The server sets no size limit of its own; MCP clients limit the size of a tool result themselves
    /// (a client moved results of 44 KB and 93 KB out of the conversation), so the default output of the list
    /// tools stays below this size for a typical repository and names the filters when it does not.</summary>
    public const int ResponseSizeHint = 32000;

    /// <summary>Applies the requested manifest count to the hard limit of the operation.
    /// A missing or non-positive request selects the limit.</summary>
    /// <param name="requested">Count requested by the caller</param>
    /// <param name="limit">Hard limit of the operation</param>
    public static int Apply(int? requested, int limit) =>
        requested is null or <= 0 ? limit : Math.Min(requested.Value, limit);
}
