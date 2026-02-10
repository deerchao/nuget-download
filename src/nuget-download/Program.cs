using System.CommandLine;

RootCommand rootCommand = new("Download nuget packages and dependencies");

var packageArgument = new Argument<string[]>("packages")
{
    Arity = ArgumentArity.OneOrMore,
    Description = "One or more package id with optional version. example: Newtonsoft.Json:13.0.1 or Autofac",
};

Option<string> outputOption = new("--output", "-o")
{
    Description = "Saving folder path, defaults to working directory",
};
Option<bool> forceOption = new("--force", "-f")
{
    Description = "Download and update existing packages",
};
Option<bool> dryRunOption = new("--dry-run")
{
    Description = "Display what will happen without actually downloading",
};


rootCommand.Arguments.Add(packageArgument);
rootCommand.Options.Add(outputOption);
rootCommand.Options.Add(forceOption);
rootCommand.Options.Add(dryRunOption);

rootCommand.SetAction(async parseResult =>
{
    var lines = parseResult.GetValue(packageArgument)!;
    var packages = lines.Select(line =>
    {
        var i = line.IndexOf(':');
        if (i < 0)
            return new RootPackage(line, null);
        return new RootPackage(line[..i], line[(i + 1)..]);
    }).ToArray();
    var d = new Downloader(packages, parseResult.GetValue(outputOption), parseResult.GetValue(forceOption),
        parseResult.GetValue(dryRunOption));
    await d.Run();
});

return rootCommand.Parse(args).Invoke();
