using System.Management.Automation;

namespace PowerServe;

[Cmdlet(VerbsLifecycle.Start, "PowerServe")]
public class StartPowerServeCmdlet : PSCmdlet
{
  [Parameter(Position = 0)]
  public string PipeName { get; set; } = "PowerServe-" + Environment.UserName;

  // Runs in the background for the life of the module
  protected override void ProcessRecord() => _ = new Server().StartAsync(PipeName!);
}