using System.Collections;
using System.Text;

namespace PowerServe.Shared;

/// <summary>
/// Provides methods for efficiently reading and writing strings to a stream with a length prefix.
/// This has the advantage of being able to use any character in the string since no delimiter is required, whereas typical methods like StringWriter.WriteLine() use newline as a delimiter and so your content cannot contain a newline.
/// Can also be used as an IEnumerable or IAsyncEnumerable to read all strings from the stream until the stream closes.
/// Autoflushes by default and does not dispose the underlying stream.
/// </summary>
public class StreamString(Stream stream, bool autoFlush = true) : IEnumerable<string>, IAsyncEnumerable<string>
{
	// We use UTF8 to optimize size, and we don't need to do any on-character splitting due to using length-based prefixes
	private readonly Encoding encoding = Encoding.UTF8;

	/// <summary>
	///	Read a string from the stream with a length prefix.
	/// </summary>
	/// <returns></returns>
	/// <exception cref="NotSupportedException">If the stream is not readable</exception>
	/// <exception cref="EndOfStreamException">If the stream ends before the result is found</exception>
	public string Read()
	{
		if (!stream.CanRead) throw new NotSupportedException("Stream is not readable");
		// Read string length prefix
		byte[] lengthBytes = new byte[sizeof(int)];
		stream.ReadExactly(lengthBytes);

		// Read string bytes
		byte[] valueBytes = new byte[BitConverter.ToInt32(lengthBytes)];
		stream.ReadExactly(valueBytes);
		return encoding.GetString(valueBytes);
	}

	/// <summary>
	/// Write a string to the stream with a length prefix. Returns the number of bytes written.
	/// </summary>
	/// <exception cref="NotSupportedException">If the stream is not writable</exception>
	/// <exception cref="EndOfStreamException">If the stream ends before the result is found</exception>
	public int Write(string value)
	{
		if (!stream.CanWrite) throw new NotSupportedException("Stream is not writable");
		// Convert string to bytes with minimal allocation
		byte[] valueBytes = encoding.GetBytes(value);

		// Write string length prefix
		stream.Write(BitConverter.GetBytes(valueBytes.Length));

		stream.Write(valueBytes);
		if (autoFlush) stream.Flush();
		return valueBytes.Length;
	}

	public async Task<string> ReadAsync(CancellationToken cancellationToken = default)
	{
		if (!stream.CanRead) throw new NotSupportedException("Stream is not readable");
		// Read string length prefix
		byte[] lengthBytes = new byte[sizeof(int)];
		await stream.ReadExactlyAsync(lengthBytes, cancellationToken);

		if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);

		// Read string bytes
		byte[] valueBytes = new byte[BitConverter.ToInt32(lengthBytes)];
		await stream.ReadExactlyAsync(valueBytes, cancellationToken);
		return encoding.GetString(valueBytes);
	}

	/// <summary>
	/// Write a string to the stream with a length prefix. Returns the number of bytes written.
	/// </summary>
	public async Task<int> WriteAsync(string value, CancellationToken cancellationToken = default)
	{

		if (!stream.CanWrite) throw new NotSupportedException("Stream is not writable");
		byte[] valueBytes = encoding.GetBytes(value);

		// Write string length prefix
		byte[] lengthBytes = BitConverter.GetBytes(valueBytes.Length);
		await stream.WriteAsync(lengthBytes, cancellationToken);

		// Write string value
		await stream.WriteAsync(valueBytes, cancellationToken);
		if (autoFlush) await FlushAsync(cancellationToken);

		return valueBytes.Length;
	}

	public void Flush() => stream.Flush();
	public Task FlushAsync(CancellationToken cancellationToken = default) => stream.FlushAsync(cancellationToken);

	#region IEnumerable/IAsyncEnumerable Implementation
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	public IEnumerator<string> GetEnumerator()
	{
		while (true)
		{
			string value;
			try
			{
				value = Read();
			}
			catch (EndOfStreamException)
			{
				yield break;
			}
			yield return value;
		}
	}

	public async IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			string value;
			try
			{
				value = await ReadAsync(cancellationToken);
			}
			catch (EndOfStreamException)
			{
				yield break;
			}
			yield return value;
		}
	}
	#endregion
}
