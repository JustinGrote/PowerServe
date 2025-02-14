using System.IO.Pipes;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading;

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
    Console.Error.WriteLine($"Script has returned {result.Count} results");
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
    if (result.Count == 1 && result[0].BaseObject is string)
    {
      Console.Error.WriteLine($"Script returned a string: {result[0].BaseObject}, passing through after removing newlines");
      return result[0].BaseObject.ToString()?.Replace("\r", "").Replace("\n", "") ?? string.Empty;
    }

    object jsonToProcess = result.Count == 1 ? result.First() : result;
    string jsonResult = JsonObject.ConvertToJson(jsonToProcess, in context);
    return errorMessage + jsonResult;
  }

  public async Task RunScriptJsonAsync(string script, StreamWriter writer, CancellationToken cancellationToken, int? depth = 5)
  {
    _ = Console.Error.WriteLineAsync($"Running script asynchronously: {script}");
    using PowerShell ps = PowerShell.Create();
    ps.RunspacePool = runspacePool;

    PSDataCollection<PSObject> outputCollection = new();
    SemaphoreSlim semaphore = new(1, 1);

    outputCollection.DataAdded += async (sender, e) =>
    {
      await semaphore.WaitAsync();
      try
      {
        var data = outputCollection.ReadAll();
        var context = new JsonObject.ConvertToJsonContext(depth ?? 5, true, true);
        foreach (var item in data)
        {
          string jsonResult = JsonObject.ConvertToJson(item, in context);
          await writer.WriteLineAsync(jsonResult);
          await writer.FlushAsync();
        }
      }
      finally
      {
        semaphore.Release();
      }
    };

    ps.AddScript(script);
    var invokeTask = ps.InvokeAsync<PSObject, PSObject>(null, outputCollection);

    using (cancellationToken.Register(() => ps.Stop()))
    {
      await invokeTask;
    }

    if (ps.HadErrors)
    {
      foreach (var error in ps.Streams.Error)
      {
        Console.Error.WriteLine($"Error: {error}");
        await writer.WriteLineAsync($"Error: {error}");
        await writer.FlushAsync();
      }
    }
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

    if (base64script == "<<CANCEL>>")
    {
      // Handle cancellation
      Console.Error.WriteLine("Cancellation requested by client.");
      return;
    }

    string script = Encoding.UTF8.GetString(Convert.FromBase64String(base64script));
    Console.Error.WriteLine($"Received script: {script}");

    try
    {
      using var cts = new CancellationTokenSource();
      var readTask = Task.Run(async () =>
      {
        while (!reader.EndOfStream)
        {
          if (await reader.ReadLineAsync() == "<<CANCEL>>")
          {
            cts.Cancel();
            break;
          }
        }
      });

      await Target.RunScriptJsonAsync(script, writer, cts.Token, 5);
      await readTask;
    }
    catch (Exception ex)
    {
      await writer.WriteLineAsync("ERROR: " + ex.Message);
    }
  }
}