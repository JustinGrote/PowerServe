using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics; // added for Trace and ConsoleTraceListener

using static PowerServe.Client;

// Write Trace to stderr

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
  eventArgs.Cancel = true;
  cts.Cancel();
};

var rootCommand = new RootCommand
{
  new Option<string>(["--script", "-c"], "The PowerShell script to execute."),
  new Option<string>(["--file", "-f"], "The path to the PowerShell script file to execute."),
  new Option<string>(["--working-directory", "-w"], () => Directory.GetCurrentDirectory(), "Specify the working directory for the PowerShell process. Defaults to the current directory."),
  new Option<string>(["--pipe-name", "-p"], () => $"PowerServe-{Environment.UserName}", "The named pipe to use. The server will start here if not already running. Defaults to PowerServe-{username}."),
  new Option<bool>(["--verbose", "-v"], "Log verbose messages about what PowerServeClient is doing to stderr. This may interfere with the JSON response so only use for troubleshooting."),
  new Option<int>(["--depth", "-d"], () => 2, "The maximum depth for JSON serialization of PowerShell objects. Defaults to 2"),
  new Option<string>(["--exeDir", "-e"], "Where to locate the PowerServe module.") {IsHidden = true}
};

rootCommand.Handler = CommandHandler.Create<string, string, string, string, bool, int, string>((script, file, workingDirectory, pipeName, verbose, depth, exeDir) =>
{
  string resolvedScript = !string.IsNullOrEmpty(file) ? $"& (Resolve-Path {file})" : script;
  return InvokeScript(
    script: resolvedScript,
    pipeName,
    workingDirectory,
    verbose,
    cts.Token,
    exeDir,
    depth
  );
});

await rootCommand.InvokeAsync(args);

Trace.Flush();