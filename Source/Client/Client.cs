using System.IO.Pipes;
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

      // While we could use some pipe existence checks, they are platform-specific, and this should only incur a small "cold-start" penalty which is why we use Connect instead
      // FIXME: There is a risk the server is unresponsive and we try to create a second listener here.
      await pipeClient.ConnectAsync(500, cancellationToken);
    }
    catch (OperationCanceledException)
    {
      throw new OperationCanceledException("Connection to PowerServe was canceled");
    }

    if (!pipeClient.IsConnected)
    {
      Trace.TraceInformation($"PowerServe is not listening on pipe {pipeName}. Spawning new pwsh.exe PowerServe listener...");

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

      try
      {
        process.Start();
        // Shouldn't take more than 3 seconds to start up
        await pipeClient.ConnectAsync(3000, cancellationToken);
      }
      catch (OperationCanceledException)
      {
        throw new OperationCanceledException("PowerServe startup was cancelled.");
      }
      finally
      {
        if (!pipeClient.IsConnected)
        {
          // Cleanup the process if running
          Trace.TraceInformation($"PowerServe did not successfully start listening on {pipeName}. Attempting to kill the process.");
          // Let these exceptions bubble up
          process.Kill();
        }
      }
    }

    if (!pipeClient.IsConnected)
    {
      throw new InvalidOperationException($"Failed to connect to PowerServe on pipe {pipeName}.");
    }

    Trace.TraceInformation($"Connected to PowerServe on Pipe {pipeName}.");

    Trace.TraceInformation($"Script contents: {script}");

    StreamString reader = new(pipeClient);
    StreamString writer = new(pipeClient);

    // Register a callback to send <<CANCEL>> when cancellation is requested
    cancellationToken.Register(() =>
      {
        Trace.TraceWarning("Cancellation requested. Sending <<CANCEL>> to server.");
        writer.Write("<<CANCEL>>");
      });

    // Send the script to the server
    string stringWithDepth = $"{depth} {script}";
    Trace.TraceInformation($"Writing to Server: {stringWithDepth}");
    int writtenBytes = await writer.WriteAsync(stringWithDepth, cancellationToken);
    Trace.TraceInformation($"Wrote {writtenBytes} bytes to server.");

    string? response;
    while ((response = reader.Read()) != null)
    {
      if (response == "<<END>>")
      {
        Trace.TraceInformation("Received <<END>>. Terminating normally.");
        break;
      }

      if (response == "<<CANCELLED>>")
      {
        Trace.TraceInformation("Script Cancelled Successfully.");
        continue;
      }

      Console.WriteLine(response);
    }

    if (response != "<<END>>")
    {
      throw new InvalidOperationException("Connection closed unexpectedly before receiving <<END>>.");
    }
  }

  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern bool GetNamedPipeServerProcessId(IntPtr hPipe, out int ClientProcessId);
}
