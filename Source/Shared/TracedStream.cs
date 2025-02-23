using System.Diagnostics;
namespace PowerServe;

public class TracedStreamReader(Stream stream) : StreamReader(stream, leaveOpen: true)
{
	public override string? ReadLine()
	{
		var line = base.ReadLine();
		Trace.TraceInformation($"READ: {line}");
		return line;
	}

	public override async Task<string?> ReadLineAsync() => await ReadLine(CancellationToken.None).AsTask();

	public override async ValueTask<string?> ReadLine(CancellationToken cancellationToken)
	{
		string? line = await base.ReadLineAsync(cancellationToken);
		Trace.TraceInformation($"READASYNC: {line}");
		return line;
	}
}

public class TracedStreamWriter : StreamWriter
{
	public TracedStreamWriter(Stream stream) : base(stream, leaveOpen: true)
	{
		AutoFlush = true;
	}

	public override void WriteLine(string? value)
	{
		Trace.TraceInformation($"WRITE: {value}");
		base.WriteLine(value);
	}

	public override async Task WriteLine(string? value)
	{
		Trace.TraceInformation($"WRITEASYNC: {value}");
		await base.WriteLineAsync(value);
	}

	public async Task WriteLine(string? value, CancellationToken cancellationToken)
	{
		Trace.TraceInformation($"WRITEASYNC: {value}");
		await base.WriteLineAsync(value.AsMemory(), cancellationToken);
	}
}
