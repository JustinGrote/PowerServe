using System.IO.Pipes;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;

using Microsoft.PowerShell.Commands;

namespace PowerServe;

public class PowerShellTarget
{
  private readonly InitialSessionState initialSessionState = InitialSessionState.CreateDefault();
  private readonly RunspacePool runspacePool;
  public PowerShellTarget() : this(null) { }
  public PowerShellTarget(int? maxRunspaces)
  {
    runspacePool = RunspaceFactory.CreateRunspacePool(initialSessionState);
    runspacePool.SetMaxRunspaces(maxRunspaces ?? Environment.ProcessorCount * 2);
    runspacePool.Open();
  }

  public string RunScriptJson(string script, int? depth = 5)
  {
    _ = Console.Error.WriteLineAsync($"Running script: {script}");
    using PowerShell ps = PowerShell.Create();
    ps.RunspacePool = runspacePool;
    var result = ps.AddScript(script).Invoke();
    Console.Error.WriteLine($"Script has returned {result.Count()} results");
    string errorMessage = string.Empty;
    if (ps.HadErrors)
    {
      foreach (var error in ps.Streams.Error)
      {
        Console.Error.WriteLine($"Error: {error}");
        errorMessage += $"Error: {error}; ";
      }
    }

    // This conversion logic is basically the same as ConvertTo-Json -Compress -Depth 5 -EnumsAsStrings
    var context = new JsonObject.ConvertToJsonContext(5, true, true);

    // Just return a string directly if that's what is emitted, for instance if the script has already JSONified its output.
    if (result.Count() == 1 && result[0].BaseObject is string)
    {
      Console.Error.WriteLine($"Script returned a string: {result[0].BaseObject}, passing through after removing newlines");
      return result[0].BaseObject.ToString()?.Replace("\r", "").Replace("\n", "") ?? string.Empty;
    }

    object jsonToProcess = result.Count() == 1 ? result.First() : result;
    string jsonResult = JsonObject.ConvertToJson(jsonToProcess, in context);
    return errorMessage + jsonResult;
  }
}

public class Server
{
  private static readonly PowerShellTarget Target = new();
  public static async Task StartAsync(string pipeName = "PowerServe")
  {
    NamedPipeServerStream serverStream = new(
      pipeName,
      PipeDirection.InOut,
      NamedPipeServerStream.MaxAllowedServerInstances,
      PipeTransmissionMode.Byte,
      PipeOptions.Asynchronous
    );

    _ = Console.Out.WriteLineAsync("Waiting for client connection...");
    await serverStream.WaitForConnectionAsync();
    _ = OnClientConnectionAsync(serverStream, pipeName);
  }

  static async Task OnClientConnectionAsync(NamedPipeServerStream serverStream, string pipeName)
  {
    _ = Console.Out.WriteLineAsync("Client connected");
    // Start the next named pipe listener. This is an eccentric behavior of named pipe servers vs. for instance tcp servers.
    _ = StartAsync(pipeName);

    using StreamReader reader = new(serverStream);
    using StreamWriter writer = new(serverStream);
    string base64script = await reader.ReadLineAsync() ?? string.Empty;
    string script = Encoding.UTF8.GetString(Convert.FromBase64String(base64script));
    try
    {
      string jsonResult = Target.RunScriptJson(script, 5);
      _ = Console.Out.WriteLineAsync($"Sending response: {jsonResult}");
      await writer.WriteLineAsync(jsonResult);
    }
    catch (Exception ex)
    {
      await writer.WriteLineAsync("ERROR: " + ex.Message);
    }
  }
}