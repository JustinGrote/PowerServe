using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics; // added for Trace and ConsoleTraceListener

using static PowerServe.Client;

// Write Trace to stderr
using ConsoleTraceListener consoleTracer = new(useErrorStream: true)
{
  Name = "mainConsoleTracer"
};

consoleTracer.WriteLine($"{DateTime.Now} [{consoleTracer.Name}] - Starting output to trace listener.");
Trace.Listeners.Add(consoleTracer);

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
  new Option<string>(["--exeDir", "-e"], "Where to locate the PowerServe module.") {IsHidden = true}
};

rootCommand.Handler = CommandHandler.Create<string, string, string, string, bool, string>((script, file, workingDirectory, pipeName, verbose, exeDir) =>
{
  string resolvedScript = !string.IsNullOrEmpty(file) ? $"& (Resolve-Path {file})" : script;
  return InvokeScript(
    script: resolvedScript,
    pipeName,
    workingDirectory,
    verbose,
    cts.Token,
    exeDir
  );
});

await rootCommand.InvokeAsync(args);

// Write final trace output and clean up the trace listener.
consoleTracer.WriteLine($"{DateTime.Now} [{consoleTracer.Name}] - Ending output to trace listener.");
Trace.Flush();