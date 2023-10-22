using System.Management.Automation;

namespace PowerServe;

[Cmdlet(VerbsLifecycle.Start, "PowerServe")]
public class StartPowerServeCmdlet : PSCmdlet
{
  [Parameter(Mandatory = true)]
  public string? PipeName { get; set; }

  protected override void ProcessRecord()
  {
    _ = Server.StartAsync(PipeName!);
  }
}