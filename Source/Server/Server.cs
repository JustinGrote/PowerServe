using System.IO.Pipes;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

using Microsoft.PowerShell.Commands;

using PowerServe.Shared;

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

  public async Task RunScriptJsonAsync(string script, Func<string, Task> writer, CancellationToken cancellationToken, int depth = 5)
  {
    _ = Console.Error.WriteLineAsync($"Running script: {script}");
    using PowerShell ps = PowerShell.Create();
    ps.RunspacePool = runspacePool;

    using PSDataCollection<PSObject> outputCollection = new() { BlockingEnumerator = true };

    cancellationToken.Register(() =>
    {
      _ = Console.Error.WriteLineAsync($"Cancellation requested. Stopping script: {script}");
      // This generates a PipelineStoppedException that must be handled
      ps.Stop();
      _ = Console.Error.WriteLineAsync($"Script Stopped");
    });

    if (cancellationToken.IsCancellationRequested)
    {
      _ = Console.Error.WriteLineAsync($"Cancellation requested. Stopping script: {script}");
      return;
    }
    var invokeTask = ps
      .AddScript(script)
      .InvokeAsync<PSObject, PSObject>(null, outputCollection);

    JsonObject.ConvertToJsonContext context = new(depth, true, true);

    // Since the collection is blocking, this should stream appropriately and in order until completed
    Task outputTask = Task.Run(async () =>
    {
      foreach (var item in outputCollection)
      {
        _ = Console.Error.WriteLineAsync($"Item Output on pipeline");
        string jsonResult = JsonObject.ConvertToJson(item, in context);
        _ = Console.Error.WriteLineAsync($"Item Received: {jsonResult}");
        await writer(jsonResult);
      }
    });

    _ = await invokeTask;
    // PowerShell doesn't auto-close the collection, we must do it manually. This will unblock the outputTask after it finishes all the items in the collection.
    outputCollection.Complete();
    await outputTask;
  }
}

public class Server
{
  private readonly PowerShellTarget Target = new();
  private readonly CancellationTokenSource CancellationTokenSource = new();
  private CancellationToken CancellationToken => CancellationTokenSource.Token;
  private int ClientSessionId = 1;

  public async Task StartAsync(string pipeName) => await StartAsync(pipeName, CancellationToken);
  public async Task StartAsync(string pipeName, CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(pipeName))
    {
      pipeName = "PowerServe-" + Environment.UserName;
    }
    Dictionary<Type, int> retryExceptions = [];
    NamedPipeServerStream? pipeServer = CreatePipe(pipeName);

    try
    {
      while (!cancellationToken.IsCancellationRequested)
      {
        try
        {
          // This should be the first await in the method so that the pipe server is running before we return to our caller.
          _ = Console.Out.WriteLineAsync($"Listening for client {ClientSessionId} on {pipeName}");
          await pipeServer.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          break;
        }
        catch (IOException ex)
        {
          retryExceptions.TryGetValue(ex.GetType(), out int retryThisExceptionType);
          retryExceptions[ex.GetType()] = retryThisExceptionType + 1;

          // The client has disconnected prematurely before WaitForConnectionAsync could pick it up.
          // Ignore that and wait for the next connection unless cancellation is requested.
          if (cancellationToken.IsCancellationRequested)
          {
            break;
          }

          await pipeServer.DisposeAsync().ConfigureAwait(false);
          pipeServer = CreatePipe(pipeName);
          continue;
        }

        // A client has connected. Open a stream to it (and possibly start listening for the next client)
        // unless cancellation is requested.
        if (!cancellationToken.IsCancellationRequested)
        {
          _ = Console.Out.WriteLineAsync($"Client {ClientSessionId} connected to {pipeName}");
          ClientSessionId++;


          // We invoke the callback in a fire-and-forget fashion as documented. It handles its own exceptions.
          _ = HandleClientConnectionAsync(pipeServer);
          // Start listening for the next client.
          pipeServer = CreatePipe(pipeName);
        }
      }
    }
    finally
    {
      if (pipeServer is not null)
      {
        await pipeServer.DisposeAsync().ConfigureAwait(false);
      }
    }

    static NamedPipeServerStream CreatePipe(string pipeName)
    {
      return new(
        pipeName,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
      );
    }
  }

  async Task HandleClientConnectionAsync(NamedPipeServerStream serverStream)
  {
    StreamString reader = new(serverStream);
    StreamString writer = new(serverStream);
    string inFromClient = await reader.ReadAsync();

    _ = Console.Out.WriteLineAsync("IN: " + inFromClient);
    // We accept from client either a depth and script or just a script. ex. 0 AAAFFESERS
    var parts = inFromClient.Split(' ', 2);
    int depth = 5; // default depth value
    string script = parts.Length == 2 && int.TryParse(parts[0], out depth) ? parts[1] : inFromClient;

    using CancellationTokenSource runScriptCts = new();
    try
    {

      Task cancellationReadTask = Task.Run(() =>
      {
        Console.Error.WriteLine("Waiting for Cancellation.");
        // This should block on the client until a new line is received or the stream is closed.
        try
        {
          if (reader.Read() == "<<CANCEL>>")
          {
            Console.Error.WriteLine("Cancel message received by client. Cancelling script.");
            runScriptCts.Cancel();
          }
          else
          {
            Console.Error.WriteLine("No cancel message received. Continuing script.");
          }
        }
        catch (EndOfStreamException)
        {
          Console.Error.WriteLine("Stream ended with no cancellation. This is normal.");
        }
      });

      await Target.RunScriptJsonAsync(
        script,
        async outLine => await writer.WriteAsync(outLine),
        runScriptCts.Token,
        depth
      );
    }
    catch (PipelineStoppedException)
    {
      if (!runScriptCts.IsCancellationRequested)
      {
        Console.Error.WriteLine($"SERVER ERROR: Script unexpectedly stopped");
      }
      Console.Error.WriteLine($"Script Succssfully Cancelled");
      await writer.WriteAsync("<<CANCELLED>>");
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"Error running script: {ex}");
      if (serverStream.CanWrite)
      {
        await writer.WriteAsync($"SERVER ERROR: {ex}");
      }
    }

    // Signal the end of the response.
    _ = Console.Out.WriteLineAsync("End of script ");
    await writer.WriteAsync("<<END>>");
  }
}