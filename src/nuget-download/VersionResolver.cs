using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

record RootPackage(string Id, string? Version);

record DependencyNode(PackageIdentity Identity, VersionRange Requirement, List<DependencyNode> Dependencies);

record DependencyInfo(string Id, VersionRange VersionRange);

class VersionResolver
{
    private readonly SourceCacheContext _cache = new() { MaxAge = DateTimeOffset.Now.AddDays(-3) };
    private readonly CancellationToken _cancellationToken = CancellationToken.None;
    private readonly SourceRepository _repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
    private readonly ILogger _logger = ConsoleLogger.Instance;
    private FindPackageByIdResource? _idResource;
    private readonly RootPackage[] _roots;

    public VersionResolver(RootPackage[] roots)
    {
        _roots = roots;
    }

    public async Task<IEnumerable<PackageIdentity>> Resolve()
    {
        var roots = await CollectDependencies();
        return ResolveVersions(roots);
    }

    private static IEnumerable<PackageIdentity> ResolveVersions(List<DependencyNode> roots)
    {
        var result = roots.Select(x => x.Identity).ToList();
        var pending = roots.SelectMany(x => x.Dependencies).ToList();
        while (pending.Count > 0)
        {
            var progress = false;
            for (var i = 0; i < pending.Count; i++)
            {
                var resolved = false;
                var node = pending[i];
                if (result.Any(x => IdEquals(x.Id, node.Identity.Id)))
                {
                    resolved = true;
                }
                else
                {
                    var allVersions = Flatten(pending, x => result.All(r => !IdEquals(r.Id, x.Identity.Id)))
                        .Where(x => IdEquals(x.Identity.Id, node.Identity.Id))
                        .Select(x => x.Identity.Version)
                        .Distinct()
                        .ToList();
                    if (allVersions.Count == 1)
                    {
                        resolved = true;
                        result.Add(node.Identity);
                        foreach (var dependency in node.Dependencies)
                            pending.AddRange(dependency);
                    }
                    else
                    {
                        var pendingVersions = pending.Where(x => IdEquals(x.Identity.Id, node.Identity.Id))
                            .Select(x => x.Identity.Version)
                            .Distinct()
                            .ToList();
                        if (pendingVersions.Count == allVersions.Count)
                        {
                            var nodes = pending.Where(x => IdEquals(x.Identity.Id, node.Identity.Id)).ToList();
                            foreach (var version in pendingVersions.Order())
                            {
                                if (nodes.All(x => x.Requirement.Satisfies(version)))
                                {
                                    resolved = true;
                                    result.Add(new(node.Identity.Id, version));
                                    foreach (var dependency in node.Dependencies)
                                        pending.AddRange(dependency);
                                    break;
                                }
                            }
                        }
                    }
                }

                if (resolved)
                {
                    pending.RemoveAt(i);
                    i--;
                    progress = true;
                }
            }

            if (!progress)
                break;
        }

        if (pending.Count > 0)
            throw new Exception(
                $"Conflicted versions found for {string.Join(", ", pending.Select(x => x.Identity.Id).Distinct(StringComparer.OrdinalIgnoreCase))}");

        return result;
    }

    private async Task<List<DependencyInfo>> GetDependencies(string id, NuGetVersion version)
    {
        var resource = await GetIdResource();
        var sets = await resource.GetDependencyInfoAsync(id, version, _cache, _logger, _cancellationToken);
        return sets.DependencyGroups.SelectMany(x => x.Packages)
            .Select(x => new DependencyInfo(x.Id, x.VersionRange))
            .Distinct()
            .ToList();
    }

    private async Task<List<NuGetVersion>> GetVersions(string id)
    {
        var resource = await GetIdResource();
        var versions = (await resource.GetAllVersionsAsync(
                id,
                _cache,
                _logger,
                _cancellationToken))
            .ToList();
        return versions;
    }

    private async Task<List<DependencyNode>> CollectDependencies()
    {
        var roots = new List<DependencyNode>();
        var identities = new List<PackageIdentity>();

        foreach (var root in _roots)
        {
            Console.WriteLine($"Resolving version for {root.Id}...");
            var versions = await GetVersions(root.Id);

            if (versions.Count == 0)
                throw new Exception($"No version found for {root.Id}");

            NuGetVersion? resolvedVersion;
            if (!string.IsNullOrEmpty(root.Version))
            {
                if (!NuGetVersion.TryParse(root.Version, out resolvedVersion))
                    throw new Exception($"Unable to parse version {root.Version} for {root.Id}");

                if (!versions.Contains(resolvedVersion))
                    throw new Exception(
                        $"Specified version {resolvedVersion} for {root.Id} not found, here are all versions available: {string.Join(", ", "versions)}")}");
            }
            else
            {
                resolvedVersion = versions.OrderDescending().FirstOrDefault(x => !x.IsPrerelease)
                                  ?? versions.First();
            }

            var identity = new PackageIdentity(root.Id, resolvedVersion);
            identities.Add(identity);
            roots.Add(new(identity, ExactVersion(resolvedVersion), await ResolveNode(root.Id, resolvedVersion)));
        }

        foreach (var node in Flatten(roots, _ => true).ToList())
            node.Dependencies.RemoveAll(x => identities.Contains(x.Identity));

        return roots;
    }

    private async Task<List<DependencyNode>> ResolveNode(string id, NuGetVersion version)
    {
        var nodes = new List<DependencyNode>();
        var identities = new List<PackageIdentity>();

        var dependencies = await GetDependencies(id, version);
        foreach (var dependency in dependencies)
        {
            var versions = await GetVersions(dependency.Id);
            var best = dependency.VersionRange.FindBestMatch(versions);
            if (best is null)
                throw new Exception(
                    $"No matched version found for dependency {dependency.Id} from {id}:{version}");

            var identity = new PackageIdentity(dependency.Id, best);
            identities.Add(identity);
            nodes.Add(new(identity, dependency.VersionRange, await ResolveNode(dependency.Id, best)));
        }

        foreach (var node in Flatten(nodes, _ => true).ToList())
            node.Dependencies.RemoveAll(x => identities.Contains(x.Identity));

        return nodes;
    }

    private async Task<FindPackageByIdResource> GetIdResource()
    {
        return _idResource ??= await _repository.GetResourceAsync<FindPackageByIdResource>(_cancellationToken);
    }

    private static bool IdEquals(string id1, string id2) => string.Equals(id1, id2, StringComparison.OrdinalIgnoreCase);

    private static VersionRange ExactVersion(NuGetVersion version)
    {
        return new VersionRange(version, true, version, true);
    }

    private static IEnumerable<DependencyNode> Flatten(IEnumerable<DependencyNode> nodes,
        Func<DependencyNode, bool> predicate) =>
        Flatten(nodes, x => x.Dependencies, predicate);

    private static IEnumerable<T> Flatten<T>(IEnumerable<T> nodes, Func<T, IEnumerable<T>> children,
        Func<T, bool> predicate)
    {
        foreach (var node in nodes)
        {
            if (!predicate(node))
                continue;

            yield return node;
            foreach (var child in Flatten(children(node), children, predicate))
                yield return child;
        }
    }
}