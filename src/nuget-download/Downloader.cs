using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

class Downloader(RootPackage[] packages, string? output, bool force, bool dryRun)
{
    private readonly SourceCacheContext _cache = new() { MaxAge = DateTimeOffset.Now.AddDays(-3) };
    private readonly CancellationToken _cancellationToken = CancellationToken.None;
    private readonly SourceRepository _repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
    private readonly ILogger _logger = ConsoleLogger.Instance;
    private FindPackageByIdResource? _idResource;

    public async Task Run()
    {
        var resolver = new VersionResolver(packages);
        var all = await resolver.Resolve();
        var downloaded = 0;
        var skipped = 0;
        foreach (var package in all)
        {
            var packageId = package.Id;
            var version = package.Version;
            var targetFileName = $"{packageId}.{version}.nupkg".ToLowerInvariant();
            var folder = string.IsNullOrEmpty(output) ? Directory.GetCurrentDirectory() : output;
            var filePath = Path.Combine(folder, targetFileName);
            var exists = File.Exists(filePath);

            if (!exists || force)
            {
                Console.WriteLine($"Downloading {packageId} {version}");
                if (!dryRun)
                {
                    using var packageStream = new MemoryStream();
                    var resource = await GetIdResource();
                    await resource.CopyNupkgToStreamAsync(
                        packageId,
                        version,
                        packageStream,
                        _cache,
                        _logger,
                        _cancellationToken);
                    packageStream.Seek(0, SeekOrigin.Begin);

                    if (exists)
                        Console.WriteLine($"Rewriting {filePath}");

                    if (!Directory.Exists(folder))
                        Directory.CreateDirectory(folder);
                    await using var file = File.Create(filePath);
                    await packageStream.CopyToAsync(file, _cancellationToken);
                }
                downloaded++;
            }
            else
            {
                Console.WriteLine($"Skipped existing {packageId} {version}");
                skipped++;
            }
        }
        
        Console.WriteLine($"Done, {downloaded} downloaded, {skipped}  skipped.");
    }

    private async Task<FindPackageByIdResource> GetIdResource()
    {
        return _idResource ??= await _repository.GetResourceAsync<FindPackageByIdResource>(_cancellationToken);
    }
}