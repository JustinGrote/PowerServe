using System.Management.Automation;

namespace PowerServe;
[Cmdlet(VerbsLifecycle.Invoke, "PowerServeCommand")]
public class InvokePowerServeCommand : Cmdlet
{
  [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
  public ScriptBlock? ScriptBlock { get; set; }

  [Parameter(Position = 1)]
  public string PipeName { get; set; } = "Test";

  protected override void ProcessRecord()
  {
    Client client = new(PipeName);
    WriteObject(client.RunScript(ScriptBlock!));
  }
}
