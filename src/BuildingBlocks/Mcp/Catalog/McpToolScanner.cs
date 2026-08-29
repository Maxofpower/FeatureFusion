using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace BuildingBlocks.Mcp.Catalog;

/// <summary>
/// Builds an immutable tool catalog from <see cref="McpToolAttribute"/> and typed <c>MapTool</c> registrations.
/// </summary>
public static class McpToolScanner
{
	/// <summary>
	/// Scans public concrete types in <paramref name="assembly"/> for <see cref="McpToolAttribute"/>.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="assembly"/> is null.</exception>
	/// <exception cref="McpCatalogException">Duplicate names, missing description, or non-constructible type.</exception>
	public static IReadOnlyList<McpToolDescriptor> Scan(Assembly assembly)
	{
		ArgumentNullException.ThrowIfNull(assembly);
		var list = new List<McpToolDescriptor>();
		foreach (var type in assembly.GetExportedTypes())
		{
			if (type.GetCustomAttribute<McpToolAttribute>() is not null)
				list.Add(FromType(type, handler: null));

			foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
			{
				if (method.GetCustomAttribute<McpToolAttribute>() is null)
					continue;
				list.Add(McpMethodTool.FromMethod(method));
			}
		}

		return list;
	}

	/// <summary>
	/// Builds a descriptor from a type that already has <see cref="McpToolAttribute"/>.
	/// </summary>
	public static McpToolDescriptor FromType(
		Type type,
		Func<IServiceProvider, object, McpInvokeContext, CancellationToken, Task<McpResult<object?>>>? handler)
	{
		ArgumentNullException.ThrowIfNull(type);
		var attr = type.GetCustomAttribute<McpToolAttribute>()
			?? throw new McpCatalogException($"Type '{type.FullName}' is missing [{nameof(McpToolAttribute)}].");

		if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
			throw new McpCatalogException($"[{nameof(McpToolAttribute)}] on '{type.FullName}' requires a concrete instantiable type.");

		if (type.GetConstructor(Type.EmptyTypes) is null && !HasParameterizedCtor(type))
			throw new McpCatalogException($"Type '{type.FullName}' must be JSON-deserializable (public constructor).");

		if (string.IsNullOrWhiteSpace(attr.Description))
			throw new McpCatalogException($"Tool '{attr.Name}' on '{type.FullName}' requires {nameof(McpToolAttribute.Description)}.");

		var kind = attr.Kind != McpToolKind.Unspecified ? attr.Kind : InferKind(type);
		if (kind == McpToolKind.Unspecified)
			throw new McpCatalogException($"Tool '{attr.Name}' must set {nameof(McpToolAttribute.Kind)} (no ICommand/IQuery markers found).");

		TimeSpan? timeout = attr.TimeoutMilliseconds > 0 ? TimeSpan.FromMilliseconds(attr.TimeoutMilliseconds) : null;

		return Create(
			attr.Name,
			attr.Description.Trim(),
			type,
			kind,
			attr.Idempotent,
			attr.AllowDryRun,
			attr.RequireConfirmation,
			timeout,
			string.IsNullOrWhiteSpace(attr.FeatureFlag) ? null : attr.FeatureFlag.Trim(),
			attr.Roles ?? [],
			handler);
	}

	/// <summary>
	/// Builds a descriptor for <c>MapTool</c> without requiring <see cref="McpToolAttribute"/> on the type.
	/// </summary>
	public static McpToolDescriptor Create(
		string name,
		string description,
		Type messageType,
		McpToolKind kind,
		bool idempotent,
		bool allowDryRun,
		bool requireConfirmation,
		TimeSpan? timeout,
		string? featureFlag,
		IReadOnlyList<string> roles,
		Func<IServiceProvider, object, McpInvokeContext, CancellationToken, Task<McpResult<object?>>>? handler)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentException.ThrowIfNullOrWhiteSpace(description);
		ArgumentNullException.ThrowIfNull(messageType);
		if (kind == McpToolKind.Unspecified)
			throw new McpCatalogException($"Tool '{name}' must have a Kind.");

		return new McpToolDescriptor
		{
			Name = name.Trim(),
			Description = description.Trim(),
			MessageType = messageType,
			Kind = kind,
			Idempotent = WriteIdempotent(kind, idempotent),
			AllowDryRun = allowDryRun,
			RequireConfirmation = requireConfirmation,
			Timeout = timeout,
			FeatureFlag = featureFlag,
			Roles = roles,
			Properties = ReadProperties(messageType),
			Handler = handler
		};
	}

	/// <summary>
	/// Ensures tool names are unique.
	/// </summary>
	/// <exception cref="McpCatalogException">Duplicate names.</exception>
	public static IReadOnlyList<McpToolDescriptor> EnsureUniqueNames(IEnumerable<McpToolDescriptor> descriptors)
	{
		ArgumentNullException.ThrowIfNull(descriptors);
		var list = descriptors.ToList();
		var dup = list.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault(g => g.Count() > 1);
		if (dup is not null)
			throw new McpCatalogException($"Duplicate MCP tool name '{dup.Key}'.");
		return list;
	}

	/// <summary>
	/// Appends <paramref name="extra"/> tools whose names are not already in <paramref name="primary"/>.
	/// Then enforces unique names.
	/// </summary>
	public static IReadOnlyList<McpToolDescriptor> MergePreferringFirst(
		IEnumerable<McpToolDescriptor> primary,
		IEnumerable<McpToolDescriptor> extra)
	{
		ArgumentNullException.ThrowIfNull(primary);
		ArgumentNullException.ThrowIfNull(extra);
		var list = primary.ToList();
		foreach (var item in extra)
		{
			if (list.Any(d => string.Equals(d.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
				continue;
			list.Add(item);
		}

		return EnsureUniqueNames(list);
	}

	/// <summary>Write idempotency applies to commands only. Queries never require <c>idempotencyKey</c>.</summary>
	internal static bool WriteIdempotent(McpToolKind kind, bool idempotentFlag)
		=> kind == McpToolKind.Command && idempotentFlag;

	internal static McpToolKind InferKind(Type type)
	{
		foreach (var iface in type.GetInterfaces())
		{
			if (iface.Namespace is not "BuildingBlocks.Mediator")
				continue;
			if (iface.IsGenericType && iface.Name == "IQuery`1")
				return McpToolKind.Query;
			if (iface.Name is "ICommand" || (iface.IsGenericType && iface.Name == "ICommand`1"))
				return McpToolKind.Command;
		}

		return McpToolKind.Unspecified;
	}

	private static bool HasParameterizedCtor(Type type)
		=> type.GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length > 0;

	private static readonly NullabilityInfoContext Nullability = new();

	private static IReadOnlyList<McpJsonProperty> ReadProperties(Type type)
	{
		var props = new List<McpJsonProperty>();
		foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
		{
			if (!prop.CanRead)
				continue;
			if (prop.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
				continue;
			if (prop.GetIndexParameters().Length > 0)
				continue;

			var enumType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
			var jsonType = ToJsonType(prop.PropertyType, prop);
			var required = IsRequired(prop);
			var desc = ReadDescription(prop);
			IReadOnlyList<string>? enumNames = null;
			IReadOnlyList<long>? enumValues = null;
			if (enumType.IsEnum)
			{
				if (UsesStringEnum(prop, enumType))
					enumNames = Enum.GetNames(enumType);
				else
					enumValues = [.. Enum.GetValues(enumType).Cast<object>().Select(v => Convert.ToInt64(v))];
			}

			props.Add(new McpJsonProperty(ToCamel(prop.Name), jsonType, required, desc)
			{
				EnumNames = enumNames,
				EnumValues = enumValues
			});
		}

		return props;
	}

	private static bool IsRequired(PropertyInfo prop)
	{
		if (prop.GetCustomAttribute<RequiredAttribute>() is not null)
			return true;
		if (prop.GetCustomAttribute<RequiredMemberAttribute>() is not null)
			return true;

		var t = prop.PropertyType;
		if (Nullable.GetUnderlyingType(t) is not null)
			return false;

		if (!t.IsValueType)
		{
			if (Nullability.Create(prop).ReadState == NullabilityState.Nullable)
				return false;
			return !HasDistinctInitializer(prop);
		}

		if (t.IsEnum)
			return false;

		return !HasDistinctInitializer(prop);
	}

	private static bool HasDistinctInitializer(PropertyInfo prop)
	{
		var declaring = prop.DeclaringType;
		if (declaring is null || declaring.IsAbstract)
			return false;
		if (declaring.GetConstructor(Type.EmptyTypes) is null)
			return false;

		object instance;
		try
		{
			instance = Activator.CreateInstance(declaring)!;
		}
		catch
		{
			return false;
		}

		var value = prop.GetValue(instance);
		object? unset = prop.PropertyType.IsValueType
			? Activator.CreateInstance(prop.PropertyType)
			: null;
		return !Equals(value, unset);
	}

	private static string? ReadDescription(PropertyInfo prop)
	{
		var tagged = prop.GetCustomAttribute<DescriptionAttribute>()?.Description;
		if (!string.IsNullOrWhiteSpace(tagged))
			return tagged.Trim();

		foreach (var attr in prop.GetCustomAttributes(inherit: true))
		{
			var name = attr.GetType().Name;
			if (name is not "SwaggerParameterAttribute" and not "SwaggerParameter")
				continue;
			var text = attr.GetType().GetProperty("Description")?.GetValue(attr) as string;
			if (!string.IsNullOrWhiteSpace(text))
				return text.Trim();
		}

		return null;
	}

	private static bool UsesStringEnum(PropertyInfo prop, Type enumType)
	{
		return IsStringEnumConverter(prop.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType)
			|| IsStringEnumConverter(enumType.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType);
	}

	private static bool IsStringEnumConverter(Type? converterType)
	{
		if (converterType is null)
			return false;
		if (converterType == typeof(JsonStringEnumConverter))
			return true;
		return converterType.IsGenericType
			&& converterType.GetGenericTypeDefinition().Name.StartsWith("JsonStringEnumConverter", StringComparison.Ordinal);
	}

	private static string ToJsonType(Type type, PropertyInfo prop)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		if (type.IsEnum)
			return UsesStringEnum(prop, type) ? "string" : "integer";
		if (type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Uri))
			return "string";
		if (type == typeof(bool))
			return "boolean";
		if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong))
			return "integer";
		if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
			return "number";
		if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string))
			return "array";
		return "object";
	}

	private static string ToCamel(string name)
		=> string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
