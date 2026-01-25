using System.Diagnostics;

namespace PowerServe.Client;

/// <summary>
/// PowerServe Client - Execute PowerShell scripts via a persistent PowerShell server.
/// </summary>
public static class Commands
{
	/// <summary>
	/// Execute a PowerShell script or file
	/// </summary>
	/// <param name="script">-c, A PowerShell script string to execute. Example: "'Hello World'"</param>
	/// <param name="file">-f, Path to a PowerShell script file to execute</param>
	/// <param name="workingDirectory">-w, The working directory to use when spawning the PowerServe server</param>
	/// <param name="pipeName">-p, The named pipe to use. The server will start here if not already running</param>
	/// <param name="verbose">-v, Log verbose messages about what PowerServeClient is doing to stderr</param>
	/// <param name="depth">-d, The maximum depth for JSON serialization of PowerShell objects</param>
	/// <param name="exeDir">-e, The directory to use to find pwsh.exe when spawning the PowerServe server if needed</param>
	public static async Task Root(
		string? script = null,
		FileInfo? file = null,
		DirectoryInfo? workingDirectory = null,
		string? pipeName = null,
		bool verbose = false,
		int depth = 2,
		DirectoryInfo? exeDir = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			// Validate file exists if provided
			if (file != null && !file.Exists)
			{
				throw new FileNotFoundException($"File not found: {file.FullName}");
			}

			// Validate working directory exists if provided
			if (workingDirectory != null && !workingDirectory.Exists)
			{
				throw new DirectoryNotFoundException($"Working directory not found: {workingDirectory.FullName}");
			}

			// Validate exeDir exists if provided
			if (exeDir != null && !exeDir.Exists)
			{
				throw new DirectoryNotFoundException($"Exe directory not found: {exeDir.FullName}");
			}

			// Set defaults
			workingDirectory ??= new DirectoryInfo(Environment.CurrentDirectory);
			pipeName ??= $"PowerServe-{Environment.UserName}";
			exeDir ??= new DirectoryInfo(Environment.CurrentDirectory);

			string resolvedScript = file != null ? $"& (Resolve-Path {file.FullName})" : script!;

			await Client.InvokeScript(
				script: resolvedScript,
				pipeName: pipeName,
				workingDirectory: workingDirectory.FullName,
				verbose: verbose,
				cancellationToken,
				exeDir.FullName,
				depth
			);
		}
		catch (Exception ex)
		{
			if (!string.IsNullOrEmpty(ex.Message))
			{
				Console.Error.WriteLine($"ERROR {ex.GetType().Name}: {ex.Message}");
			}
			Environment.ExitCode = (int)ExitCodeMapper.GetExitCode(ex);
			throw;
		}
		finally
		{
			Trace.Flush();
		}
	}
}

