using System.Security.Cryptography;
using System.Text;

namespace BuildingBlocks.Idempotency;

/// <summary>Validates raw <c>Idempotency-Key</c> header values.</summary>
public static class IdempotencyKeyValidator
{
	/// <summary>
	/// Validates <paramref name="rawKey"/> against <paramref name="options"/>.
	/// </summary>
	/// <returns><c>true</c> when valid; otherwise <c>false</c> with a human-readable <paramref name="error"/>.</returns>
	public static bool TryValidate(string? rawKey, IdempotencyOptions options, out string? error)
	{
		ArgumentNullException.ThrowIfNull(options);

		var headerName = options.HeaderName;

		if (rawKey is null)
		{
			error = $"The {headerName} header is missing.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(rawKey))
		{
			error = $"The {headerName} value cannot be empty.";
			return false;
		}

		if (ContainsControlCharacters(rawKey))
		{
			error = $"The {headerName} value contains invalid control characters.";
			return false;
		}

		var max = options.MaxKeyLength > 0 ? options.MaxKeyLength : 256;
		if (rawKey.Length > max)
		{
			error = $"The {headerName} value exceeds the maximum length of {max}.";
			return false;
		}

		if (options.RequireUlid && !Ulid.TryParse(rawKey, out _))
		{
			error = $"Invalid {headerName} format: {rawKey}";
			return false;
		}

		error = null;
		return true;
	}

	private static bool ContainsControlCharacters(string value)
	{
		foreach (var ch in value)
		{
			if (char.IsControl(ch))
				return true;
		}

		return false;
	}
}

/// <summary>Computes request fingerprints for opt-in payload binding.</summary>
public static class IdempotencyFingerprint
{
	/// <summary>
	/// SHA-256 hex of <c>method + "\n" + path + "\n" + body</c>.
	/// </summary>
	public static async Task<string> ComputeAsync(
		string method,
		string path,
		Stream body,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(method);
		ArgumentNullException.ThrowIfNull(path);
		ArgumentNullException.ThrowIfNull(body);

		using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

		AppendUtf8(hasher, method);
		hasher.AppendData("\n"u8);
		AppendUtf8(hasher, path);
		hasher.AppendData("\n"u8);

		var buffer = new byte[8192];
		int read;
		while ((read = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
			hasher.AppendData(buffer.AsSpan(0, read));

		Span<byte> hash = stackalloc byte[32];
		if (!hasher.TryGetHashAndReset(hash, out var written) || written != 32)
			throw new CryptographicException("Failed to compute fingerprint hash.");

		return Convert.ToHexString(hash);
	}

	/// <summary>Constant-time string compare for hex fingerprints.</summary>
	public static bool FixedTimeEquals(string a, string b)
	{
		var ba = Encoding.UTF8.GetBytes(a);
		var bb = Encoding.UTF8.GetBytes(b);
		return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
	}

	private static void AppendUtf8(IncrementalHash hasher, string value)
	{
		var byteCount = Encoding.UTF8.GetByteCount(value);
		if (byteCount <= 256)
		{
			Span<byte> scratch = stackalloc byte[256];
			var written = Encoding.UTF8.GetBytes(value, scratch);
			hasher.AppendData(scratch[..written]);
			return;
		}

		hasher.AppendData(Encoding.UTF8.GetBytes(value));
	}
}
