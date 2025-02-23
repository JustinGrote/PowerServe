namespace PowerServe.Shared;

/// <summary>
/// A dictionary that can be serialized to a string format.
/// </summary>
public class OptionsDictionary : Dictionary<string, string>
{
	public OptionsDictionary() : base() { }

	// Create from existing dictionary
	public OptionsDictionary(IDictionary<string, string> dictionary) : base(dictionary) { }

	// Serialize to string format (key1=value1;key2=value2)
	public override string ToString()
	{
		return string.Join(";", this.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
	}

	// Parse from string format
	public static OptionsDictionary FromString(string input)
	{
		var result = new OptionsDictionary();
		if (string.IsNullOrEmpty(input)) return result;

		foreach (var pair in input.Split(';'))
		{
			var parts = pair.Split('=');
			if (parts.Length == 2)
			{
				result[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
			}
		}
		return result;
	}
}

/// <summary>
/// Options for script invocation that can be serialized to a string format.
/// </summary>
public class ScriptInvocationOptions(int Depth = 2)
{
	public OptionsDictionary ToOptionsDictionary()
	{
		return new OptionsDictionary
		{
			{ nameof(Depth), Depth.ToString() }
		};
	}

	public static ScriptInvocationOptions FromOptionsDictionary(OptionsDictionary options)
	{
		var result = new ScriptInvocationOptions();
		if (options.TryGetValue(nameof(Depth), out string? depthString))
		{
			result.Depth = int.Parse(depthString);
		}
		return result;
	}
}
