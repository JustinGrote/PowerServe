using System.IO.Pipes;
using System.Text;
using System.Diagnostics;

if (args.Length == 0)
{
  Console.Error.WriteLine("Please provide a script as a quoted argument.");
  return;
}
string script = args[0];

string pipeName = "PowerServe-" + Environment.UserName; //TODO: Make this an argument parameter
using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);

try
{
  pipeClient.Connect(500);
}
catch (TimeoutException)
{
  Console.Error.WriteLine($"PowerServe is not running. Spawning new pwsh.exe process to listen on named pipe {pipeName}...");
  string exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
  Console.Error.WriteLine($"Current dir: {exeDir}");
  ProcessStartInfo startInfo = new()
  {
    FileName = "pwsh.exe",
    CreateNoWindow = true,
    UseShellExecute = false,
    // WindowStyle = ProcessWindowStyle.Hidden,
    WorkingDirectory = exeDir,
    ArgumentList = {
      "-NoProfile",
      "-NoExit",
      "-NonInteractive",
      "-Command", $"Import-Module $pwd/PowerServe.dll;Start-PowerServe -PipeName {pipeName}" }
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