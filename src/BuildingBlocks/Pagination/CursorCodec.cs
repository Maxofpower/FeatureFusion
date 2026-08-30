using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildingBlocks.Pagination;

internal sealed class CursorPayload
{
	public int V { get; set; } = 1;
	public string Fp { get; set; } = "";
	public PageDirection Walk { get; set; }
	public List<CursorValue> Vals { get; set; } = [];
}

internal sealed class CursorValue
{
	public string T { get; set; } = "";
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonElement? V { get; set; }
}

/// <summary>Encode/decode opaque keyset cursors.</summary>
public static class CursorCodec
{
	/// <summary>Returns true when <paramref name="cursor"/> is missing.</summary>
	/// <param name="cursor">Raw cursor.</param>
	public static bool IsEmpty(string? cursor) => string.IsNullOrWhiteSpace(cursor);

	internal static string Encode<T>(
		SortKey<T> sortKey,
		ReadOnlySpan<object?> values,
		PageDirection walk,
		PaginationOptions options)
	{
		options.ValidateSigning();
		var vals = new List<CursorValue>(values.Length);
		for (var i = 0; i < values.Length; i++)
		{
			vals.Add(ToCursorValue(values[i]));
		}

		var payload = new CursorPayload
		{
			V = 1,
			Fp = sortKey.Fingerprint,
			Walk = walk,
			Vals = vals
		};
		var json = JsonSerializer.SerializeToUtf8Bytes(payload, CursorJsonContext.Default.CursorPayload);
		var body = Base64Url.Encode(json);
		if (options.SigningKey is { Length: > 0 } key)
		{
			Span<byte> mac = stackalloc byte[32];
			HMACSHA256.HashData(key, json, mac);
			return string.Concat("v1.", body, ".", Base64Url.Encode(mac));
		}

		return string.Concat("v1.", body);
	}

	internal static DecodedCursor Decode<T>(string cursor, SortKey<T> sortKey, PaginationOptions options)
	{
		options.ValidateSigning();
		if (IsEmpty(cursor))
		{
			throw new PaginationException(PaginationErrorCode.InvalidCursor, "Cursor is empty.");
		}

		if (!TrySplitCursor(cursor.AsSpan(), out var payloadSpan, out var macSpan))
		{
			throw new PaginationException(PaginationErrorCode.InvalidCursor, "Cursor format is invalid.");
		}

		byte[] json;
		try
		{
			json = Base64Url.Decode(payloadSpan);
		}
		catch
		{
			throw new PaginationException(PaginationErrorCode.InvalidCursor, "Cursor payload is not valid Base64url.");
		}

		if (options.SigningKey is { Length: > 0 } key)
		{
			if (macSpan.IsEmpty)
			{
				throw new PaginationException(
					PaginationErrorCode.InvalidCursor,
					"Cursor is unsigned but HMAC signing is enabled.");
			}

			byte[] mac;
			try
			{
				mac = Base64Url.Decode(macSpan);
			}
			catch
			{
				throw new PaginationException(PaginationErrorCode.InvalidCursor, "Cursor MAC is not valid Base64url.");
			}

			Span<byte> expected = stackalloc byte[32];
			HMACSHA256.HashData(key, json, expected);
			if (mac.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(expected, mac))
			{
				throw new PaginationException(PaginationErrorCode.InvalidCursor, "Cursor HMAC mismatch.");
			}
		}

		CursorPayload? payload;
		try
		{
			payload = JsonSerializer.Deserialize(json.AsSpan(), CursorJsonContext.Default.CursorPayload);
		}
		catch
		{
			throw new PaginationException(PaginationErrorCode.InvalidCursor, "Cursor JSON is invalid.");
		}

		if (payload is null || payload.V != 1 || payload.Vals.Count != sortKey.Slots.Count)
		{
			throw new PaginationException(PaginationErrorCode.InvalidCursor, "Cursor payload version or arity is invalid.");
		}

		if (!string.Equals(payload.Fp, sortKey.Fingerprint, StringComparison.Ordinal))
		{
			throw new PaginationException(
				PaginationErrorCode.CursorSortMismatch,
				"Cursor sort key does not match the requested SortKey.");
		}

		var values = new object?[payload.Vals.Count];
		for (var i = 0; i < payload.Vals.Count; i++)
		{
			try
			{
				values[i] = FromCursorValue(payload.Vals[i], sortKey.Slots[i].ClrType);
			}
			catch (PaginationException)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new PaginationException(
					PaginationErrorCode.InvalidCursor,
					"Cursor value could not be decoded for the sort-key slot.",
					ex);
			}
		}

		return new DecodedCursor(values, payload.Walk);
	}

	/// <summary>
	/// Decodes <paramref name="cursor"/> against <paramref name="sortKey"/>. Empty cursors succeed.
	/// Throws <see cref="PaginationException"/> on invalid format, HMAC failure, or fingerprint mismatch.
	/// </summary>
	/// <typeparam name="T">Row type.</typeparam>
	/// <param name="cursor">Raw cursor; null or whitespace is valid (first page).</param>
	/// <param name="sortKey">Expected sort key.</param>
	/// <param name="options">Options (signing key).</param>
	public static void Validate<T>(string? cursor, SortKey<T> sortKey, PaginationOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(sortKey);
		if (IsEmpty(cursor))
		{
			return;
		}

		_ = Decode(cursor!, sortKey, options ?? PaginationOptions.Default);
	}

	/// <summary>Validates format and optional HMAC without checking the sort fingerprint.</summary>
	/// <param name="cursor">Raw cursor.</param>
	/// <param name="options">Options (signing key).</param>
	public static bool TryValidateFormat(string? cursor, PaginationOptions? options = null)
	{
		if (IsEmpty(cursor))
		{
			return true;
		}

		try
		{
			options ??= PaginationOptions.Default;
			if (!TrySplitCursor(cursor.AsSpan(), out var payloadSpan, out var macSpan))
			{
				return false;
			}

			var json = Base64Url.Decode(payloadSpan);
			_ = JsonSerializer.Deserialize(json.AsSpan(), CursorJsonContext.Default.CursorPayload);
			if (options.SigningKey is { Length: > 0 } key)
			{
				if (macSpan.IsEmpty)
				{
					return false;
				}

				var mac = Base64Url.Decode(macSpan);
				Span<byte> expected = stackalloc byte[32];
				HMACSHA256.HashData(key, json, expected);
				return mac.Length == expected.Length && CryptographicOperations.FixedTimeEquals(expected, mac);
			}

			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TrySplitCursor(
		ReadOnlySpan<char> cursor,
		out ReadOnlySpan<char> payload,
		out ReadOnlySpan<char> mac)
	{
		payload = default;
		mac = default;
		if (!cursor.StartsWith("v1.", StringComparison.Ordinal))
		{
			return false;
		}

		var rest = cursor[3..];
		if (rest.IsEmpty)
		{
			return false;
		}

		var dot = rest.IndexOf('.');
		if (dot < 0)
		{
			payload = rest;
			return true;
		}

		payload = rest[..dot];
		mac = rest[(dot + 1)..];
		return !payload.IsEmpty && !mac.IsEmpty && mac.IndexOf('.') < 0;
	}

	private static CursorValue ToCursorValue(object? value)
	{
		if (value is null)
		{
			return new CursorValue { T = "null" };
		}

		if (value is DateTime dateTime)
		{
			return new CursorValue
			{
				T = typeof(DateTime).FullName!,
				V = JsonSerializer.SerializeToElement(ToUtc(in dateTime))
			};
		}

		var type = value.GetType();
		if (type.IsEnum)
		{
			return new CursorValue
			{
				T = "enum",
				V = JsonSerializer.SerializeToElement(Convert.ToInt64(value))
			};
		}

		return new CursorValue
		{
			T = type.FullName ?? type.Name,
			V = JsonSerializer.SerializeToElement(value)
		};
	}

	private static object? FromCursorValue(CursorValue value, Type clrType)
	{
		if (value.T == "null" || value.V is null)
		{
			return null;
		}

		var json = value.V.Value;
		if (value.T == "enum" || value.T.StartsWith("enum:", StringComparison.Ordinal))
		{
			var enumType = clrType;
			if (value.T.StartsWith("enum:", StringComparison.Ordinal))
			{
				enumType = Type.GetType(value.T["enum:".Length..], throwOnError: false) ?? clrType;
			}

			enumType = Nullable.GetUnderlyingType(enumType) ?? enumType;
			var numeric = json.Deserialize<long>();
			return Enum.ToObject(enumType, numeric);
		}

		var target = Nullable.GetUnderlyingType(clrType) ?? clrType;
		var decoded = json.Deserialize(target);
		if (decoded is DateTime dateTime)
		{
			return ToUtc(in dateTime);
		}

		return decoded;
	}

	private static DateTime ToUtc(in DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
		};
}

internal sealed record DecodedCursor(object?[] Values, PageDirection Walk);

internal static class Base64Url
{
	public static string Encode(ReadOnlySpan<byte> data)
	{
		var maxChars = ((data.Length + 2) / 3) * 4;
		char[]? rented = null;
		Span<char> chars = maxChars <= 512
			? stackalloc char[maxChars]
			: (rented = ArrayPool<char>.Shared.Rent(maxChars));
		try
		{
			if (!Convert.TryToBase64Chars(data, chars, out var written))
			{
				throw new FormatException("Base64 encode failed.");
			}

			var span = chars[..written];
			while (span.Length > 0 && span[^1] == '=')
			{
				span = span[..^1];
			}

			for (var i = 0; i < span.Length; i++)
			{
				span[i] = span[i] switch
				{
					'+' => '-',
					'/' => '_',
					var c => c
				};
			}

			return new string(span);
		}
		finally
		{
			if (rented is not null)
			{
				ArrayPool<char>.Shared.Return(rented);
			}
		}
	}

	public static byte[] Decode(string text) => Decode(text.AsSpan());

	public static byte[] Decode(ReadOnlySpan<char> text)
	{
		var pad = (4 - (text.Length % 4)) % 4;
		var paddedLen = text.Length + pad;
		char[]? rentedChars = null;
		Span<char> padded = paddedLen <= 512
			? stackalloc char[paddedLen]
			: (rentedChars = ArrayPool<char>.Shared.Rent(paddedLen));
		try
		{
			text.CopyTo(padded);
			for (var i = 0; i < text.Length; i++)
			{
				padded[i] = padded[i] switch
				{
					'-' => '+',
					'_' => '/',
					var c => c
				};
			}

			for (var i = text.Length; i < paddedLen; i++)
			{
				padded[i] = '=';
			}

			var maxBytes = (paddedLen / 4) * 3;
			byte[]? rentedBytes = null;
			Span<byte> bytes = maxBytes <= 512
				? stackalloc byte[maxBytes]
				: (rentedBytes = ArrayPool<byte>.Shared.Rent(maxBytes));
			try
			{
				if (!Convert.TryFromBase64Chars(padded[..paddedLen], bytes, out var written))
				{
					throw new FormatException("Base64url payload is invalid.");
				}

				return bytes[..written].ToArray();
			}
			finally
			{
				if (rentedBytes is not null)
				{
					ArrayPool<byte>.Shared.Return(rentedBytes);
				}
			}
		}
		finally
		{
			if (rentedChars is not null)
			{
				ArrayPool<char>.Shared.Return(rentedChars);
			}
		}
	}
}
