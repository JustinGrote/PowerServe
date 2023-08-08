using System.IO.Pipes;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

using StreamJsonRpc;

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
    return ps.AddScript(script).Invoke<object>();
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

    // Bind the connection to the JsonRpc Instance
    JsonRpc clientSession = JsonRpc.Attach(serverStream, new PowerShellTarget());
    await clientSession.Completion;
  }
}