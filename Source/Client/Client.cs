using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;

using PowerServe.Shared;

using static PowerServe.Shared.NamedPipeUtils;

namespace PowerServe.Client;

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

    // Check if the named pipe client exists

    using var pipeClient = new NamedPipeClientStream(
      ".",
      pipeName,
      PipeDirection.InOut,
      PipeOptions.Asynchronous
    );

    // TODO: Implement a command line option to end server at the end of invocation

    // TODO: Implement a command line option to not automatically start a server but instead fail if not connected
    if (!NamedPipeExists(pipeName))
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
      }
      catch (OperationCanceledException)
      {
        throw new OperationCanceledException("PowerServe startup was cancelled.");
      }

      Trace.TraceInformation($"Started PowerServe listener with PID {process.Id}.");
    }

    try
    {
      // TODO: Make timeout configurable via command line option
      await pipeClient.ConnectAsync(3000, cancellationToken);
    }
    catch (OperationCanceledException)
    {
      throw new OperationCanceledException("Connection to PowerServe was canceled");
    }
    catch (TimeoutException)
    {
      throw new TimeoutException($"PowerServe connection failed to pipe {pipeName}.");
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

      string[] responseParts = response.Split(':', 2);
      if (responseParts.Length != 2 && responseParts[0].Length != 1)
      {
        throw new InvalidOperationException($"Invalid message format from server: {response}");
      }

      string type = responseParts[0];
      string message = responseParts[1];

      switch (type)
      {
        case "O":
          Console.WriteLine(message);
          continue;
        case "D":
          Console.Error.WriteLine($"DEBUG: {message}");
          continue;
        case "V":
          Console.Error.WriteLine($"VERBOSE: {message}");
          continue;
        case "I":
          Console.Error.WriteLine($"INFO: {message}");
          continue;
        case "W":
          Console.Error.WriteLine($"WARNING: {message}");
          continue;
        case "E":
          Console.Error.WriteLine($"ERROR: {message}");
          continue;
        default:
          throw new InvalidOperationException($"Invalid message type from server: {type}");
      }
    }

    if (response != "<<END>>")
    {
      throw new InvalidOperationException("Connection closed unexpectedly before receiving <<END>>.");
    }
  }


  [DllImport("kernel32.dll", SetLastError = true)]
  public static extern bool GetNamedPipeServerProcessId(IntPtr hPipe, out int ClientProcessId);
}
