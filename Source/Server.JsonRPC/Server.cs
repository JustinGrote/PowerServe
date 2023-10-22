using System.IO.Pipes;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text.Json;
namespace PowerServe;

public interface Target
{
  public IEnumerable<object> RunScript(string script);
}

public class PowerShellTarget : Target
{
  private readonly PowerShell ps;
  public PowerShellTarget()
  {
    RunspacePool runspacePool = RunspaceFactory.CreateRunspacePool();
    runspacePool.Open();
    ps = PowerShell.Create();
    ps.RunspacePool = runspacePool;
  }

  public IEnumerable<object> RunScript(string script)
  {
    Console.WriteLine($"Running script: {script}");
    Object result = ps.AddScript(script).Invoke<object>();
    string jsonResult = JsonSerializer.Serialize(result);
    return jsonResult;
  }
}

public static class Server
{
  public static async Task StartAsync(string pipeName = "test")
  {
    NamedPipeServerStream serverStream = new(
      pipeName,
      PipeDirection.InOut,
      NamedPipeServerStream.MaxAllowedServerInstances,
      PipeTransmissionMode.Byte,
      PipeOptions.Asynchronous
    );

    PowerShellTarget target = new()

    Console.WriteLine("Waiting connected");
    await serverStream.WaitForConnectionAsync();
    _ = OnClientConnectionAsync(serverStream, pipeName).ContinueWith(
      _ => Console.WriteLine("Client Disconnected"),
      TaskScheduler.Default
    );
  }

  static async Task OnClientConnectionAsync(NamedPipeServerStream serverStream, string pipeName)
  {
    Console.WriteLine("Client connected");
    // Start the next named pipe listener
    _ = StartAsync(pipeName);

    using StreamReader reader = new(serverStream);
    using StreamWriter writer = new(serverStream);
    string base64script = await reader.ReadLineAsync();
    string script = Encoding.UTF8.GetString(Convert.FromBase64String(base64Script));
    RunScript(script)

  }
}