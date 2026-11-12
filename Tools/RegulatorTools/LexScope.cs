using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PayrollEngine.Lex;
using PayrollEngine.Mcp.Core.Isolation;

namespace PayrollEngine.Mcp.Tools.RegulatorTools;

/// <summary>A manifest file inside the workspace</summary>
/// <param name="LexPath">Full path of lex.json</param>
/// <param name="Relative">Path relative to its workspace root, with <c>/</c> separators</param>
/// <param name="Repository">Repository folder name</param>
/// <param name="Satellite">Name of the folder that holds the .lex folder</param>
internal sealed record LexManifestRef(string LexPath, string Relative, string Repository, string Satellite);

/// <summary>The manifests selected by the scope of a call</summary>
/// <param name="Manifests">The manifests to process, at most the limit of the call</param>
/// <param name="Matched">Number of manifests in the scope</param>
/// <param name="Limit">Maximum manifests of the call</param>
/// <param name="Skipped">Manifest files left out because they leave the workspace (links)</param>
internal sealed record LexScope(IReadOnlyList<LexManifestRef> Manifests, int Matched, int Limit, int Skipped)
{
    /// <summary>True when the scope holds more manifests than the call processes</summary>
    public bool Truncated => Matched > Manifests.Count;
}

/// <summary>Resolves the scope of a call (root, repository, satellite) to manifest files below the workspace roots.
/// Every manifest path is resolved through the workspace, so the roots, the repository mask and the link rules apply.</summary>
internal static class LexScopeResolver
{
    /// <summary>File name of a manifest</summary>
    internal const string ManifestFileName = "lex.json";

    /// <summary>Resolves the manifests of a scope</summary>
    /// <param name="workspace">The workspace of the deployment</param>
    /// <param name="root">A configured workspace root, null for all roots</param>
    /// <param name="repository">Repository folder name below the root, null for all repositories matching the mask</param>
    /// <param name="satellite">Satellite folder name, null for all satellites</param>
    /// <param name="limit">Maximum manifests of the call</param>
    /// <param name="requireNarrowScope">The call needs a repository or a satellite (operations with network access)</param>
    internal static LexScope Resolve(WorkspaceContext workspace, string root, string repository, string satellite,
        int limit, bool requireNarrowScope = false)
    {
        if (!workspace.IsConfigured)
        {
            throw new InvalidOperationException(
                $"No workspace roots configured ({WorkspaceContext.SectionName}:Roots).");
        }
        repository = NullIfBlank(repository);
        satellite = NullIfBlank(satellite);
        if (requireNarrowScope && repository == null && satellite == null)
        {
            throw new ArgumentException(
                "This operation reads sources over the network: set 'repository' or 'satellite' to narrow the scope.");
        }
        if (repository != null)
        {
            if (repository.IndexOfAny(['/', '\\']) >= 0)
            {
                throw new ArgumentException("'repository' must be a folder name without separators.");
            }
            // the path rules of the workspace (repository mask, parent segments) decide about the name
            workspace.ResolvePath(repository);
        }

        var rootFolders = new List<string>();
        if (NullIfBlank(root) != null)
        {
            var resolved = workspace.ResolvePath(root);
            if (!resolved.IsRoot)
            {
                throw new ArgumentException("'root' must be a configured workspace root.");
            }
            rootFolders.Add(resolved.FullPath);
        }
        else
        {
            rootFolders.AddRange(workspace.Roots.Select(x => workspace.ResolvePath(x).FullPath).Distinct());
        }

        var matched = new List<LexManifestRef>();
        var skipped = 0;
        foreach (var rootFolder in rootFolders)
        {
            foreach (var repositoryFolder in Repositories(workspace, rootFolder, repository))
            {
                var repositoryName = Path.GetFileName(repositoryFolder);
                var files = FileResolver.Resolve(Path.Combine(repositoryFolder, ManifestFileName), recursive: true);
                foreach (var file in files)
                {
                    var manifest = Describe(workspace, file, repositoryName, ref skipped);
                    if (manifest != null &&
                        (satellite == null || string.Equals(manifest.Satellite, satellite, StringComparison.OrdinalIgnoreCase)))
                    {
                        matched.Add(manifest);
                    }
                }
            }
        }
        return new LexScope(matched.Take(limit).ToList(), matched.Count, limit, skipped);
    }

    /// <summary>Resolves a single manifest from a path argument: a lex.json file, a .lex folder,
    /// or a satellite folder holding .lex/lex.json</summary>
    /// <param name="workspace">The workspace of the deployment</param>
    /// <param name="path">Path argument of the caller</param>
    internal static LexManifestRef ResolveManifest(WorkspaceContext workspace, string path)
    {
        var resolved = workspace.ResolvePath(path);
        var lexPath = FileResolver.ResolveManifest(resolved.FullPath);
        if (lexPath == null || !string.Equals(Path.GetFileName(lexPath), ManifestFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException($"No {ManifestFileName} found at '{path}'.");
        }
        var skipped = 0;
        return Describe(workspace, lexPath, resolved.Repository, ref skipped)
               ?? throw new UnauthorizedAccessException($"Access denied: path '{path}' leaves the workspace.");
    }

    private static IEnumerable<string> Repositories(WorkspaceContext workspace, string rootFolder, string repository)
    {
        var names = Directory.GetDirectories(rootFolder)
            .Where(folder => workspace.IsRepositoryName(Path.GetFileName(folder)))
            .Where(folder => repository == null ||
                             string.Equals(Path.GetFileName(folder), repository, StringComparison.OrdinalIgnoreCase))
            .ToList();
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static LexManifestRef Describe(WorkspaceContext workspace, string lexPath, string repository, ref int skipped)
    {
        WorkspacePath resolved;
        try
        {
            resolved = workspace.ResolvePath(lexPath);
        }
        catch (UnauthorizedAccessException)
        {
            // a link leads out of the workspace
            skipped++;
            return null;
        }
        var lexDirectory = Path.GetDirectoryName(resolved.FullPath)!;
        var satelliteFolder = Directory.GetParent(lexDirectory)?.FullName ?? lexDirectory;
        return new LexManifestRef(
            resolved.FullPath,
            resolved.RelativePath.Replace(Path.DirectorySeparatorChar, '/'),
            resolved.Repository ?? repository,
            Path.GetFileName(satelliteFolder));
    }

    private static string NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
