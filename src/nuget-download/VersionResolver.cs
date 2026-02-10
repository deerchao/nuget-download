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
    private readonly SourceCacheContext _cache = new();
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
        var roots = await ResolveRoots();
        var result = new List<PackageIdentity>();
        var all = Flatten(roots).OrderBy(x => x.Identity).ToList();
        foreach (var group in all.GroupBy(x => x.Identity.Id))
        {
            var id = group.Key;
            var nodes = group.ToList();
            var versions = nodes.Select(x => x.Identity.Version).Distinct().ToList();
            if (versions.Count == 1)
            {
                result.Add(new(id, versions[0]));
            }
            else
            {
                var resolved = false;
                foreach (var version in versions)
                {
                    var satisfies = true;
                    foreach (var node in nodes)
                    {
                        if (!node.Requirement.Satisfies(version))
                        {
                            satisfies = false;
                            break;
                        }
                    }

                    if (satisfies)
                    {
                        result.Add(new(id, version));
                        resolved = true;
                        break;
                    }
                }

                if (!resolved)
                    throw new Exception($"Conflicted versions found for {id}");
            }
        }

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

    private async Task<List<DependencyNode>> ResolveRoots()
    {
        var directs = new List<PackageIdentity>();
        var nodes = new List<DependencyNode>();
        foreach (var root in _roots)
        {
            Console.WriteLine($"Resolving version for {root.Id}...");
            var versions = await GetVersions(root.Id);

            if (versions.Count == 0)
                throw new Exception($"No versions found for {root.Id}");

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
            directs.Add(identity);
            nodes.Add(new(identity, ExactVersion(resolvedVersion), await ResolveNode(root.Id, resolvedVersion)));
        }

        foreach (var node in Flatten(nodes).ToList())
            node.Dependencies.RemoveAll(x => directs.Contains(x.Identity));

        return nodes;
    }

    private async Task<List<DependencyNode>> ResolveNode(string id, NuGetVersion version)
    {
        var dependencies = await GetDependencies(id, version);
        var nodes = new List<DependencyNode>();
        var directs = new List<PackageIdentity>();

        foreach (var dependency in dependencies)
        {
            var versions = await GetVersions(dependency.Id);
            var best = dependency.VersionRange.FindBestMatch(versions);
            if (best is null)
                throw new Exception(
                    $"No matched version found for dependency {dependency.Id} from {id}:{version}");

            var identity = new PackageIdentity(dependency.Id, best);
            directs.Add(identity);
            nodes.Add(new(identity, dependency.VersionRange, await ResolveNode(dependency.Id, best)));
        }

        foreach (var node in Flatten(nodes).ToList())
            node.Dependencies.RemoveAll(x => directs.Contains(x.Identity));

        return nodes;
    }

    private async Task<FindPackageByIdResource> GetIdResource()
    {
        return _idResource ??= await _repository.GetResourceAsync<FindPackageByIdResource>(_cancellationToken);
    }

    private static VersionRange ExactVersion(NuGetVersion version)
    {
        return new VersionRange(version, true, version, true);
    }

    private static IEnumerable<DependencyNode> Flatten(IEnumerable<DependencyNode> nodes) =>
        Flatten(nodes, x => x.Dependencies);

    private static IEnumerable<T> Flatten<T>(IEnumerable<T> nodes, Func<T, IEnumerable<T>> children)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(children(node), children))
                yield return child;
        }
    }
}