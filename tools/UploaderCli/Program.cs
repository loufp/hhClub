using System.CommandLine;
using Ci_Cd.Services;

var root = new RootCommand("Uploader CLI for Artifactory and GitHub Releases");

var fileOpt = new Option<string>("--file", "Path to artifact file") { IsRequired = true };
var modeOpt = new Option<string>("--mode", () => "artifactory", "Mode: artifactory|github");
var argsOpt = new Option<string[]>("--args", () => Array.Empty<string>(), "Additional args: artifactory -> <url> <repo> <user> <pass>; github -> <owner/repo> <token>");

root.AddOption(fileOpt);
root.AddOption(modeOpt);
root.AddOption(argsOpt);

root.SetHandler(async (file, mode, args) =>
{
    var svc = new UploaderService(new System.Net.Http.HttpClient());
    if (mode == "artifactory")
    {
        if (args.Length < 4) { Console.Error.WriteLine("artifactory requires <url> <repo> <user> <pass>"); return; }
        var res = await svc.UploadToArtifactoryAsync(file, args[0], args[1], args[2], args[3]);
        Console.WriteLine($"Success: {res.Success}, Message: {res.Message}");
    }
    else if (mode == "github")
    {
        if (args.Length < 2) { Console.Error.WriteLine("github requires <owner/repo> <token>"); return; }
        var res = await svc.UploadToGitHubReleaseAsync(file, args[0], args[1]);
        Console.WriteLine($"Success: {res.Success}, Message: {res.Message}");
    }
    else
    {
        Console.Error.WriteLine("Unknown mode");
    }
}, fileOpt, modeOpt, argsOpt);

return await root.InvokeAsync(args);

