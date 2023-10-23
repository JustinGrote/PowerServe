using CommandLine;

using System.Diagnostics.CodeAnalysis;
using PowerServe;

/// <summary>
/// Defines the available command line options for the PowerServeClient.
/// </summary>
public class CliOptions
{
  [Value(0, Required = true, MetaName = "Script", HelpText = "The PowerShell script to execute.")]
  public string Script { get; set; } = string.Empty;

  [Option('p', "pipe-name", Required = false, HelpText = "The name of the named pipe to use.")]
  public string PipeName { get; set; } = $"PowerServe-{Environment.UserName}";

  [Option('v', "verbose", Required = false, HelpText = "Log verbose messages about what PowerServeClient is doing to stderr. This may interfere with the JSON response so only use for troubleshooting.")]
  public bool Debug { get; set; }
}

// We cant use top-level main because for .NET 8 AOT we need this additional attribute to make CommandLineParser work with AOT
static class Program
{
  [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CliOptions))]
  static async Task Main(string[] args)
  {
    // Program entrypoint
    await Parser.Default.ParseArguments<CliOptions>(args)
      .WithParsedAsync(options => Client.InvokeScript(options.Script, options.PipeName, options.Debug));
  }
}