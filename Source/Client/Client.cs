using System.IO.Pipes;
using System.Text;
using System.Diagnostics;

namespace PowerServe;
class Client
{
  public static async Task InvokeScript(string script, string pipeName, bool debug)
  {
    using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);

    try
    {
      pipeClient.Connect(500);
    }
    catch (TimeoutException)
    {
      if (debug) Console.Error.WriteLine($"PowerServe is not running on pipe {pipeName}. Spawning new pwsh.exe PowerServe listener...");

      string? exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
      ProcessStartInfo startInfo = new()
      {
        FileName = "pwsh.exe",
        CreateNoWindow = true,
        UseShellExecute = false,
        WindowStyle = ProcessWindowStyle.Hidden,
        WorkingDirectory = exeDir,
        ArgumentList =
        {
          "-NoProfile",
          "-NoExit",
          "-NonInteractive",
          "-Command", $"Import-Module $pwd/PowerServe.dll;Start-PowerServe -PipeName {pipeName}"
        }
      };
      Process process = new()
      {
        StartInfo = startInfo
      };
      process.Start();
      // Shouldn't take more than 3000 seconds to start up
      pipeClient.Connect(3000);
    }

    // We use base64 encoding to avoid issues with newlines in the script.
    byte[] scriptBytes = Encoding.UTF8.GetBytes(script);
    string base64Script = Convert.ToBase64String(scriptBytes);

    var streamWriter = new StreamWriter(pipeClient);
    await streamWriter.WriteLineAsync(base64Script);
    await streamWriter.FlushAsync();

    var streamReader = new StreamReader(pipeClient);
    string? jsonResponse = await streamReader.ReadLineAsync();

    if (jsonResponse == null)
    {
      Console.Error.WriteLine("No response received.");
      Environment.Exit(2);
    }

    Console.WriteLine(jsonResponse);
  }
}
