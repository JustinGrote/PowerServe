namespace PowerServe.Shared;

public static class NamedPipeUtils
{
	/// <summary>
	/// Check if a named pipe exists in a cross-platform manner. Reimplementation of a private .NET core method.
	/// </summary>
	public static bool NamedPipeExists(string pipeName)
	{
		if (Path.IsPathFullyQualified(pipeName))
		{
			string directory = Path.GetDirectoryName(pipeName) ?? throw new InvalidOperationException("IsPathFullyQualified was true but GetDirectoryName returned null. This is a bug and should not happen.");
			string fileName = Path.GetFileName(pipeName);
			return Directory.GetFiles(directory, fileName).Length > 0;
		}

		return Directory.GetFiles(
			OperatingSystem.IsWindows() ? @"\\.\pipe\" : Path.GetTempPath(),
			OperatingSystem.IsWindows() ? pipeName : $"CoreFxPipe_{pipeName}"
		).Length > 0;
	}
}