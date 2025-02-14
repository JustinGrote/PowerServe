using System.IO.Pipes;
using System.Text;
using System.Diagnostics;

namespace PowerServe;
static class Client
{
  /// <summary>
  /// An invocation of the client. We connect, send the script, and receive the response in JSONLines format.
  /// </summary>
  public static async Task InvokeScript(string script, string pipeName, string? workingDirectory, bool debug, CancellationToken cancellationToken, string? exeDir)
  {
    using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);

    try
    {
      pipeClient.Connect(500);
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

    var streamWriter = new StreamWriter(pipeClient);
    var streamReader = new StreamReader(pipeClient);

    // Register a callback to send <<CANCEL>> when cancellation is requested
    cancellationToken.Register(async () =>
    {
      Trace.TraceWarning("Cancellation requested. Sending <<CANCEL>> to server.");
      await streamWriter.WriteLineAsync("<<CANCEL>>");
      await streamWriter.FlushAsync();
    });

    Trace.TraceInformation($"Base64 Encoded Script: {base64Script}");
    await streamWriter.WriteLineAsync(base64Script);
    await streamWriter.FlushAsync();

    string? jsonResponse = await streamReader.ReadLineAsync();
    Trace.TraceInformation($"Received JSON response: {jsonResponse}");

    if (jsonResponse == null)
    {
      Console.Error.WriteLine("No response received.");
      Environment.Exit(2);
    }

    Console.WriteLine(jsonResponse);
  }
}
