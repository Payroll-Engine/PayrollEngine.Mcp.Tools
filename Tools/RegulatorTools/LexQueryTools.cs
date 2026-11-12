using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PayrollEngine.Lex;
using PayrollEngine.Mcp.Core;
using PayrollEngine.Mcp.Core.Isolation;

namespace PayrollEngine.Mcp.Tools.RegulatorTools;

/// <summary>Read-only MCP tools for the provenance manifests (lex.json) of data regulations.
/// The tools read files below the configured workspace roots and never write.
/// Registered only when the permission of the Regulator role is not None.</summary>
[McpServerToolType]
[ToolRole(McpRole.Regulator)]
// ReSharper disable once UnusedType.Global
public sealed class LexQueryTools(IsolationContext isolation) : IsolationToolBase(isolation)
{
    private const string ModeDevelopment = "development";
    private const string ModeRelease = "release";
    private const string SourceLocal = "local";
    private const string SourceLive = "live";

    #region lex_list_manifests

    /// <summary>List the manifests of the workspace with their summary</summary>
    [McpServerTool(Name = "lex_list_manifests", ReadOnly = true), Description(
        "List the lex manifests (lex.json) below the configured workspace roots with a summary per manifest: " +
        "entries, open entries, verification counts (auto, manual, without), sources, files, exclusions, " +
        "snapshot state (missing, current, stale, unreadable) and the content hash. " +
        "Narrow the scope with root, repository and satellite. " +
        "The response reports matched, processed and limit; a truncated scope needs a narrower scope.")]
    public async Task<string> ListManifestsAsync(
        [Description("A configured workspace root; default all roots")] string root = null,
        [Description("Repository folder name below the root; default all repositories matching the repository mask")] string repository = null,
        [Description("Satellite folder name (the folder that holds .lex); default all satellites")] string satellite = null,
        [Description("Maximum manifests of the call; default and maximum 200")] int? maxManifests = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            RequireRead();
            var scope = LexScopeResolver.Resolve(Workspace, root, repository, satellite,
                RegulatorLimits.Apply(maxManifests, RegulatorLimits.ListManifests));
            var source = new WorkspaceLexSource(scope.Manifests.Select(x => x.LexPath));
            var infos = await source.ListManifestsAsync(cancellationToken);

            var manifests = scope.Manifests.Select((manifest, index) =>
            {
                var info = infos[index];
                return new
                {
                    manifest = manifest.Relative,
                    manifest.Repository,
                    manifest.Satellite,
                    info.Error,
                    info.Authority,
                    info.Regulation,
                    info.ValidFrom,
                    info.Provider,
                    info.Languages,
                    info.Entries,
                    info.OpenEntries,
                    info.AutoVerified,
                    info.ManualVerified,
                    info.WithoutVerification,
                    info.Sources,
                    info.Files,
                    info.Exclusions,
                    snapshotState = info.SnapshotState,
                    snapshotDifferences = info.SnapshotDifferences,
                    contentHash = info.ContentHash
                };
            }).ToList();

            return SignWithIntegrity(RegulatorJson.Serialize(new
            {
                scope = DescribeScope(scope),
                manifests
            }));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { return Error(exception); }
    }

    #endregion

    #region lex_get_entry

    /// <summary>Get one entry of a manifest</summary>
    [McpServerTool(Name = "lex_get_entry", ReadOnly = true), Description(
        "Get one lex entry with its context: content, source URLs, declared sources, source location and quote, " +
        "legal reference, verification record, matching exclusions, review rule and the state against the snapshot " +
        "(noSnapshot, notInSnapshot, changed, current). The content hash identifies the version of lex.json that was read.")]
    public async Task<string> GetEntryAsync(
        [Description("Manifest path as returned by lex_list_manifests (lex.json, the .lex folder or the satellite folder)")] string manifest,
        [Description("The entry key, e.g. 'RateTable.A'")] string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            RequireRead();
            ArgumentException.ThrowIfNullOrWhiteSpace(manifest);
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            var reference = LexScopeResolver.ResolveManifest(Workspace, manifest);
            var source = new WorkspaceLexSource([reference.LexPath]);
            var info = await source.GetManifestAsync(reference.LexPath, cancellationToken);
            if (info?.Error != null)
            {
                throw new InvalidOperationException($"Manifest '{reference.Relative}' cannot be read: {info.Error}");
            }
            var entry = await source.GetEntryAsync(reference.LexPath, key.Trim(), cancellationToken)
                        ?? throw new InvalidOperationException($"Entry '{key}' not found in manifest '{reference.Relative}'.");

            return SignWithIntegrity(RegulatorJson.Serialize(new
            {
                manifest = reference.Relative,
                entry.Key,
                entry.Entry,
                entry.Sources,
                entry.Review,
                entry.Exclusions,
                snapshotState = entry.SnapshotState,
                contentHash = entry.ContentHash
            }));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { return Error(exception); }
    }

    #endregion

    #region lex_check

    /// <summary>Check manifests against their data regulation</summary>
    [McpServerTool(Name = "lex_check", ReadOnly = true), Description(
        "Check lex manifests against the lookup values of their data regulation: coverage, orphan entries, value " +
        "differences, traceability fields, evidence files, snapshot and validFrom. " +
        "Mode 'development' (default) reports the structural findings; mode 'release' is the release gate: location and " +
        "verification are required and the entries are verified against their local evidence (secondary main source, " +
        "entries that need a manual verification, zero values without justification, unused evidence are findings). " +
        "checkUrls requests all source URLs and compares live sources with the snapshot; it needs a repository or satellite. " +
        "Returns the structured result per manifest and the totals. Nothing is written.")]
    public async Task<string> CheckAsync(
        [Description("A configured workspace root; default all roots")] string root = null,
        [Description("Repository folder name below the root; default all repositories matching the repository mask")] string repository = null,
        [Description("Satellite folder name (the folder that holds .lex); default all satellites")] string satellite = null,
        [Description("Check mode: 'development' (default) or 'release'")] string mode = ModeDevelopment,
        [Description("Request the source URLs and compare live sources with the snapshot (network access, narrow scope required)")] bool checkUrls = false,
        [Description("Development mode: a missing source location is a finding")] bool requireLocation = false,
        [Description("Development mode: an entry without verification record is a finding")] bool requireVerification = false,
        [Description("Maximum manifests of the call; limits: development 50, release 25, with URL check 10")] int? maxManifests = null,
        IProgress<ProgressNotificationValue> progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            RequireRead();
            var release = ParseMode(mode);
            var limit = checkUrls ? RegulatorLimits.CheckUrls : release ? RegulatorLimits.CheckRelease : RegulatorLimits.Check;
            var scope = LexScopeResolver.Resolve(Workspace, root, repository, satellite,
                RegulatorLimits.Apply(maxManifests, limit), requireNarrowScope: checkUrls);

            using var urlChecker = checkUrls ? new UrlChecker() : null;
            var results = new List<JsonNode>();
            var checkResults = new List<CheckResult>();
            for (var index = 0; index < scope.Manifests.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manifest = scope.Manifests[index];
                Report(progress, index, scope.Manifests.Count,
                    $"{(checkUrls ? "Checking URLs of" : "Checking")} {manifest.Relative}");
                var result = await CheckFileAsync(manifest, requireLocation, requireVerification, release, urlChecker);
                checkResults.Add(result);
                var node = RegulatorJson.ToNode(result);
                node["lexPath"] = manifest.Relative;
                node["manifest"] = manifest.Relative;
                results.Add(node);
            }
            Report(progress, scope.Manifests.Count, scope.Manifests.Count, "Check completed");

            return SignWithIntegrity(RegulatorJson.Serialize(new
            {
                scope = DescribeScope(scope),
                mode = release ? ModeRelease : ModeDevelopment,
                urlsChecked = checkUrls,
                summary = new
                {
                    withoutFindings = checkResults.Count(x => !x.HasFindings),
                    withFindings = checkResults.Count(x => x.HasFindings),
                    verifiedAuto = checkResults.Sum(x => x.VerifiedAuto),
                    verifiedManual = checkResults.Sum(x => x.VerifiedManual),
                    unverified = checkResults.Sum(x => x.Unverified),
                    open = checkResults.Sum(x => x.Open)
                },
                results
            }));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { return Error(exception); }
    }

    private static async Task<CheckResult> CheckFileAsync(LexManifestRef reference, bool requireLocation,
        bool requireVerification, bool release, UrlChecker urlChecker)
    {
        Manifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<Manifest>(
                await File.ReadAllTextAsync(reference.LexPath), RegulatorJson.InputOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new CheckResult
            {
                LexPath = reference.LexPath,
                Satellite = reference.Satellite,
                Error = "lex.json could not be parsed: " + exception.Message
            };
        }
        manifest ??= new Manifest();
        var result = Checker.Check(manifest, reference.LexPath, requireLocation, requireVerification, release);
        if (release)
        {
            await Checker.CheckSources(manifest, result);
        }
        if (urlChecker != null)
        {
            await urlChecker.Apply(manifest, result);
        }
        return result;
    }

    #endregion

    #region lex_verify_snapshot

    /// <summary>Verify the snapshots of manifests</summary>
    [McpServerTool(Name = "lex_verify_snapshot", ReadOnly = true), Description(
        "Verify the snapshot (snapshot.json) of each manifest against the manifest and the evidence files in the .lex folder. " +
        "State per manifest: current (no difference), stale (differences listed), missing or unreadable. Nothing is written.")]
    public async Task<string> VerifySnapshotAsync(
        [Description("A configured workspace root; default all roots")] string root = null,
        [Description("Repository folder name below the root; default all repositories matching the repository mask")] string repository = null,
        [Description("Satellite folder name (the folder that holds .lex); default all satellites")] string satellite = null,
        [Description("Maximum manifests of the call; default and maximum 100")] int? maxManifests = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            RequireRead();
            var scope = LexScopeResolver.Resolve(Workspace, root, repository, satellite,
                RegulatorLimits.Apply(maxManifests, RegulatorLimits.FileChecks));
            var results = new List<object>();
            foreach (var manifest in scope.Manifests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await VerifySnapshotFileAsync(manifest));
            }
            return SignWithIntegrity(RegulatorJson.Serialize(new
            {
                scope = DescribeScope(scope),
                results
            }));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { return Error(exception); }
    }

    private static async Task<object> VerifySnapshotFileAsync(LexManifestRef reference)
    {
        var lexDirectory = Path.GetDirectoryName(reference.LexPath)!;
        var snapshotPath = Path.Combine(lexDirectory, SnapshotOperation.SnapshotFileName);
        if (!File.Exists(snapshotPath))
        {
            return SnapshotResult(reference, LexSnapshotState.Missing, []);
        }
        try
        {
            var manifest = JsonSerializer.Deserialize<Manifest>(
                await File.ReadAllTextAsync(reference.LexPath), RegulatorJson.InputOptions) ?? new Manifest();
            var snapshot = JsonSerializer.Deserialize<Snapshot>(
                await File.ReadAllTextAsync(snapshotPath), RegulatorJson.InputOptions);
            if (snapshot == null)
            {
                return SnapshotResult(reference, LexSnapshotState.Unreadable, ["snapshot.json is empty"]);
            }
            var differences = Verifier.Verify(manifest, snapshot, lexDirectory);
            return SnapshotResult(reference,
                differences.Count == 0 ? LexSnapshotState.Current : LexSnapshotState.Stale, differences);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return SnapshotResult(reference, LexSnapshotState.Unreadable, [exception.Message]);
        }
    }

    private static object SnapshotResult(LexManifestRef reference, LexSnapshotState state, List<string> differences) => new
    {
        manifest = reference.Relative,
        reference.Satellite,
        state,
        differenceCount = differences.Count,
        differences = differences.Take(RegulatorLimits.MaxDifferences).ToList(),
        differencesTruncated = differences.Count > RegulatorLimits.MaxDifferences
    };

    #endregion

    #region lex_verify_source

    /// <summary>Verify the entries against their sources without writing</summary>
    [McpServerTool(Name = "lex_verify_source", ReadOnly = true), Description(
        "Verify the numbers of each lex entry against its sources and rate every entry: found, ambiguous, partial, " +
        "notFound, quoteNotFound, unreadable, noNumbers, noContent or noEvidence. " +
        "Source 'local' (default) reads the evidence files in the .lex folder; source 'live' requests the source URLs " +
        "(network access, needs a repository or satellite). Locations and verification records are never written. " +
        "By default only the entries with a rating other than 'found' are listed; the counts cover all entries.")]
    public async Task<string> VerifySourceAsync(
        [Description("A configured workspace root; default all roots")] string root = null,
        [Description("Repository folder name below the root; default all repositories matching the repository mask")] string repository = null,
        [Description("Satellite folder name (the folder that holds .lex); default all satellites")] string satellite = null,
        [Description("Verify against 'local' evidence (default) or 'live' sources")] string source = SourceLocal,
        [Description("List only the entries that are not rated 'found' (default true)")] bool onlyProblems = true,
        [Description("Maximum manifests of the call; limits: local 25, live 5")] int? maxManifests = null,
        IProgress<ProgressNotificationValue> progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            RequireRead();
            var live = ParseSource(source);
            var limit = live ? RegulatorLimits.VerifySourceLive : RegulatorLimits.VerifySourceLocal;
            var scope = LexScopeResolver.Resolve(Workspace, root, repository, satellite,
                RegulatorLimits.Apply(maxManifests, limit), requireNarrowScope: live);

            var vocabulary = VerifySourceOperation.ResolveVocabulary(null, Environment.GetEnvironmentVariable);
            using var client = live ? SourceDocument.CreateHttpClient() : null;
            var documents = new Dictionary<string, SourceDocument>();
            var results = new List<object>();
            for (var index = 0; index < scope.Manifests.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manifest = scope.Manifests[index];
                Report(progress, index, scope.Manifests.Count,
                    $"Verifying {(live ? "live sources of" : "local evidence of")} {manifest.Relative}");
                results.Add(await VerifySourceFileAsync(manifest, vocabulary, client, documents, onlyProblems));
            }
            Report(progress, scope.Manifests.Count, scope.Manifests.Count, "Source verification completed");

            return SignWithIntegrity(RegulatorJson.Serialize(new
            {
                scope = DescribeScope(scope),
                source = live ? SourceLive : SourceLocal,
                written = false,
                results
            }));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { return Error(exception); }
    }

    private static async Task<object> VerifySourceFileAsync(LexManifestRef reference, string vocabulary,
        System.Net.Http.HttpClient client, Dictionary<string, SourceDocument> documents, bool onlyProblems)
    {
        try
        {
            var file = await VerifySourceOperation.VerifyFileAsync(reference.LexPath, vocabulary,
                client != null ? url => SourceDocument.LoadLive(url, client) : null, documents);
            var counts = file.Results
                .GroupBy(x => x.Match)
                .OrderBy(x => x.Key)
                .ToDictionary(x => JsonNamingPolicy.CamelCase.ConvertName(x.Key.ToString()), x => x.Count());
            var listed = file.Results
                .Where(x => !onlyProblems || x.Match != SourceMatch.Found)
                .ToList();
            return new
            {
                manifest = reference.Relative,
                reference.Satellite,
                entries = file.Results.Count,
                counts,
                problems = file.Problems,
                ambiguous = file.Ambiguous,
                languages = file.Languages,
                unusedEvidence = file.UnusedEvidence,
                listedEntries = Math.Min(listed.Count, RegulatorLimits.MaxEntriesPerManifest),
                entriesTruncated = listed.Count > RegulatorLimits.MaxEntriesPerManifest,
                results = listed.Take(RegulatorLimits.MaxEntriesPerManifest).ToList()
            };
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new { manifest = reference.Relative, reference.Satellite, error = exception.Message };
        }
    }

    #endregion

    #region lex_validate

    /// <summary>Validate the structure of manifests</summary>
    [McpServerTool(Name = "lex_validate", ReadOnly = true), Description(
        "Validate the structure of lex manifests: required fields, duplicate keys, content and contentFile, declared " +
        "sources and files. Returns the list of errors per manifest; an empty list means the manifest is valid. " +
        "Nothing is written.")]
    public async Task<string> ValidateAsync(
        [Description("A configured workspace root; default all roots")] string root = null,
        [Description("Repository folder name below the root; default all repositories matching the repository mask")] string repository = null,
        [Description("Satellite folder name (the folder that holds .lex); default all satellites")] string satellite = null,
        [Description("Maximum manifests of the call; default and maximum 100")] int? maxManifests = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            RequireRead();
            var scope = LexScopeResolver.Resolve(Workspace, root, repository, satellite,
                RegulatorLimits.Apply(maxManifests, RegulatorLimits.FileChecks));
            var results = new List<ValidationResult>();
            foreach (var reference in scope.Manifests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await ValidateFileAsync(reference));
            }
            return SignWithIntegrity(RegulatorJson.Serialize(new
            {
                scope = DescribeScope(scope),
                valid = results.Count(x => x.Valid),
                invalid = results.Count(x => !x.Valid),
                results
            }));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { return Error(exception); }
    }

    private static async Task<ValidationResult> ValidateFileAsync(LexManifestRef reference)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<Manifest>(
                await File.ReadAllTextAsync(reference.LexPath), RegulatorJson.InputOptions) ?? new Manifest();
            var errors = Validator.Validate(manifest, Path.GetDirectoryName(reference.LexPath)!);
            return new ValidationResult(reference.Relative, reference.Satellite, errors.Count == 0, errors);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ValidationResult(reference.Relative, reference.Satellite, false,
                ["lex.json could not be parsed: " + exception.Message]);
        }
    }

    #endregion

    #region Helpers

    // the properties are read by the JSON serializer only
    // ReSharper disable NotAccessedPositionalProperty.Local
    private sealed record ValidationResult(string Manifest, string Satellite, bool Valid, List<string> Errors);
    // ReSharper restore NotAccessedPositionalProperty.Local

    /// <summary>The Regulator role is registered at Read at least; direct callers get the same rule</summary>
    private void RequireRead()
    {
        if (!IsGranted(McpRole.Regulator, McpPermission.Read))
        {
            throw new UnauthorizedAccessException("The Regulator role is not permitted (McpServer:Permissions:Regulator).");
        }
    }

    private static object DescribeScope(LexScope scope) => new
    {
        matched = scope.Matched,
        processed = scope.Manifests.Count,
        limit = scope.Limit,
        truncated = scope.Truncated,
        skippedOutsideWorkspace = scope.Skipped
    };

    private static bool ParseMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode) || string.Equals(mode.Trim(), ModeDevelopment, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (string.Equals(mode.Trim(), ModeRelease, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        throw new ArgumentException($"Unknown mode '{mode}' (allowed: {ModeDevelopment}, {ModeRelease}).");
    }

    private static bool ParseSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || string.Equals(source.Trim(), SourceLocal, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (string.Equals(source.Trim(), SourceLive, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        throw new ArgumentException($"Unknown source '{source}' (allowed: {SourceLocal}, {SourceLive}).");
    }

    private static void Report(IProgress<ProgressNotificationValue> progress, int done, int total, string message) =>
        progress?.Report(new ProgressNotificationValue { Progress = done, Total = total, Message = message });

    #endregion
}
