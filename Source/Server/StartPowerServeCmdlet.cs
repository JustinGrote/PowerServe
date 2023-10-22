using System.Management.Automation;
using System.Runtime.InteropServices.ComTypes;

namespace PowerServe;

[Cmdlet(VerbsLifecycle.Start, "PowerServe")]
public class StartPowerServeCmdlet : PSCmdlet
{
  [Parameter(Position = 0, Mandatory = true)]
  public string? PipeName { get; set; }

  protected override void ProcessRecord() => _ = Server.StartAsync(PipeName!);
}