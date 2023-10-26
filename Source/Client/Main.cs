using CommandLine;
using static PowerServe.Client;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Defines the available command line options for the PowerServeClient.
/// </summary>
public class CliOptions
{
  [Option('c', "Script", SetName = "Script", Required = true, HelpText = "The PowerShell script to execute.")]
  public string Script { get; set; } = string.Empty;

  [Option('f', "File", SetName = "File", Required = true, HelpText = "The path to the PowerShell script file to execute.")]
  public string File { get; set; } = string.Empty;

  [Option('w', "WorkingDirectory", Required = false, HelpText = "Specify the working directory for the PowerShell process. Defaults to the current directory.")]
  public string? WorkingDirectory { get; set; }

  [Option('p', "pipe-name", Required = false, HelpText = "The named pipe to use. The server will start here if not already running. Defaults to PowerServe-{username}.")]
  public string PipeName { get; set; } = $"PowerServe-{Environment.UserName}";

  [Option('v', "verbose", Required = false, HelpText = "Log verbose messages about what PowerServeClient is doing to stderr. This may interfere with the JSON response so only use for troubleshooting.")]
  public bool Debug { get; set; }
}


static class Program
{
  // We cant use top-level main because for .NET 8 AOT we need this additional attribute to make CommandLineParser work with AOT
  [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CliOptions))]
  static async Task Main(string[] args)
  {
    // Program entrypoint
    await Parser.Default.ParseArguments<CliOptions>(args)
      .WithParsedAsync(options =>
      {
        string script = options.File != string.Empty ? $"& (Resolve-Path {options.File})" : options.Script;
        return InvokeScript(script, options.WorkingDirectory, options.PipeName, options.Debug);
      });
  }
}