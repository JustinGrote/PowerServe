using System.IO.Pipes;
using System.Management.Automation;
using StreamJsonRpc;

namespace PowerServe;

public class Client
{
  private readonly string _pipeName;

  public Client(string pipeName = "powerserve")
  {
    _pipeName = pipeName;
  }

  public IEnumerable<PSObject> RunScript(ScriptBlock scriptBlock)
  {
    return RunScriptAsync<PSObject>(scriptBlock.ToString()).GetAwaiter().GetResult();
  }

  public IEnumerable<PSObject> RunScript(string script)
  {
    return RunScriptAsync<PSObject>(script).GetAwaiter().GetResult();
  }

  public async Task<IEnumerable<PSObject>> RunScriptAsync(string script)
  {
    return await RunScriptAsync<PSObject>(script);
  }

  public async Task<IEnumerable<T>> RunScriptAsync<T>(string script)
  {
    using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync();
    using JsonRpc client = JsonRpc.Attach(pipe);
    return await client.InvokeAsync<IEnumerable<T>>("RunScript", script);
  }
}

