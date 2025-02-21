using System.IO.Pipes;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PowerServe;
static class Client
{
  /// <summary>
  /// An invocation of the client. We connect, send the script, and receive the response in JSONLines format.
  /// </summary>
  public static async Task InvokeScript(string script, string pipeName, string? workingDirectory, bool verbose, CancellationToken cancellationToken, string? exeDir, int depth)
  {
    if (verbose)
    {
      // Log all tracing to stderr.
      Trace.Listeners.Add(new ConsoleTraceListener(useErrorStream: true));
    }

    using var pipeClient = new NamedPipeClientStream(
      ".",
      pipeName,
      PipeDirection.InOut,
      PipeOptions.Asynchronous
    );

    try
    {
      pipeClient.Connect(500);
      GetNamedPipeServerProcessId(pipeClient.SafePipeHandle.DangerousGetHandle(), out int serverProcessId);
      Trace.TraceInformation($"Connected to PowerServe (PID: {serverProcessId}) on Pipe {pipeName}.");
    }
    catch (TimeoutException)
    {
      Trace.TraceInformation($"PowerServe is not running on pipe {pipeName}. Spawning new pwsh.exe PowerServe listener...");

      if (string.IsNullOrWhiteSpace(exeDir))
      {
        exeDir = Environment.GetEnvironmentVariable("POWERSERVE_EXE_DIR")
        ?? Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName)
        ?? Environment.CurrentDirectory;
      }

      ProcessStartInfo startInfo = new()
      {
        FileName = "pwsh",
        CreateNoWindow = true,
        UseShellExecute = false,
        WindowStyle = ProcessWindowStyle.Hidden,
        WorkingDirectory = workingDirectory ?? exeDir,
        ArgumentList =
        {
          "-NoProfile",
          "-NoExit",
          "-NonInteractive",
          "-Command", $"Import-Module $(Join-Path (Resolve-Path '{exeDir}') 'PowerServe.dll');Start-PowerServe -PipeName {pipeName}"
        }
      };
      Process process = new()
      {
        StartInfo = startInfo
      };
      process.Start();
      // Shouldn't take more than 3 seconds to start up
      pipeClient.Connect(3000);
    }

    if (!pipeClient.IsConnected)
    {

      throw new InvalidOperationException($"Failed to connect to PowerServe on pipe {pipeName}");
    }

    Trace.TraceInformation($"Connected to PowerServe on Pipe {pipeName}.");

    // We use base64 encoding to avoid issues with newlines in the script.
    Trace.TraceInformation($"Script contents: {script}");

    byte[] scriptBytes = Encoding.UTF8.GetBytes(script);
    string base64Script = Convert.ToBase64String(scriptBytes);

    var streamWriter = new TracedStreamWriter(pipeClient);
    var streamReader = new TracedStreamReader(pipeClient);

    // Register a callback to send <<CANCEL>> when cancellation is requested
    cancellationToken.Register(() =>
    {
      Trace.TraceWarning("Cancellation requested. Sending <<CANCEL>> to server.");
      streamWriter.WriteLine("<<CANCEL>>");
      streamWriter.Flush();
    });

    if (depth > 0)
    {
      Trace.TraceInformation($"Depth specified as {depth}. Appending to script.");
      base64Script = $"{base64Script} {depth}";
    }
    Trace.TraceInformation($"Base64 Encoded Script: {base64Script}");

    await streamWriter.WriteLineAsync(base64Script);
    await streamWriter.FlushAsync();

    string? jsonResponse;
    while ((jsonResponse = await streamReader.ReadLineAsync()) != null)
    {
      if (jsonResponse == "<<END>>")
      {
        Trace.TraceInformation("Received <<END>>. Terminating normally.");
        break;
      }

      if (jsonResponse == "<<CANCELLED>>")
      {
        Trace.TraceInformation("Script Cancelled Successfully.");
        continue;
      }

      Console.WriteLine(jsonResponse);
    }

    if (jsonResponse != "<<END>>")
    {
      throw new InvalidOperationException("Connection closed unexpectedly before receiving <<END>>.");
    }
  }

  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern bool GetNamedPipeServerProcessId(IntPtr hPipe, out int ClientProcessId);
}
