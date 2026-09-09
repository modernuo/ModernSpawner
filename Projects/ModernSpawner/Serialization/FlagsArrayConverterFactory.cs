using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server.Engines.ModernSpawner.Serialization;

/// <summary>
/// JSON converter factory for [Flags] enum types.
/// Serializes flags as an array of enum value names.
/// </summary>
public class FlagsArrayConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsEnum && typeToConvert.IsDefined(typeof(FlagsAttribute), false);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(FlagsArrayConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>
/// Generic JSON converter for [Flags] enum types.
/// Serializes as an array of enum value names, e.g. ["Sunday", "Monday", "Friday"]
/// </summary>
public class FlagsArrayConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected array for flags enum {typeof(T).Name}");
        }

        var flags = 0ul;
        var underlyingType = Enum.GetUnderlyingType(typeof(T));

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected string value in flags array for {typeof(T).Name}");
            }

            var name = reader.GetString();
            if (!Enum.TryParse<T>(name, ignoreCase: true, out var val))
            {
                throw new JsonException($"Unknown enum value '{name}' for {typeof(T).Name}");
            }

            flags |= ConvertToUInt64(underlyingType, val);
        }

        return ConvertFromUInt64(underlyingType, flags);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        var underlyingType = Enum.GetUnderlyingType(typeof(T));
        var intValue = ConvertToUInt64(underlyingType, value);

        foreach (var flagName in Enum.GetNames(typeof(T)))
        {
            var flagValue = Enum.Parse<T>(flagName, false);
            var flag = ConvertToUInt64(underlyingType, flagValue);

            // Only write single-bit flags that are set (skip multi-bit values like "All" or "Weekdays")
            if (flag > 0 && (flag & (flag - 1)) == 0 && (intValue & flag) == flag)
            {
                writer.WriteStringValue(flagName);
            }
        }

        writer.WriteEndArray();
    }

    private static ulong ConvertToUInt64(Type underlyingType, object value) =>
        Type.GetTypeCode(underlyingType) switch
        {
            TypeCode.SByte => (ulong)(sbyte)value,
            TypeCode.Byte => (byte)value,
            TypeCode.Int16 => (ulong)(short)value,
            TypeCode.UInt16 => (ushort)value,
            TypeCode.Int32 => (ulong)(int)value,
            TypeCode.UInt32 => (uint)value,
            TypeCode.Int64 => (ulong)(long)value,
            TypeCode.UInt64 => (ulong)value,
            _ => throw new InvalidOperationException($"Unsupported underlying type for enum")
        };

    private static T ConvertFromUInt64(Type underlyingType, ulong flags)
    {
        switch (Type.GetTypeCode(underlyingType))
        {
            case TypeCode.SByte:
                {
                    var num = (sbyte)flags;
                    return Unsafe.As<sbyte, T>(ref num);
                }
            case TypeCode.Byte:
                {
                    var num = (byte)flags;
                    return Unsafe.As<byte, T>(ref num);
                }
            case TypeCode.Int16:
                {
                    var num = (short)flags;
                    return Unsafe.As<short, T>(ref num);
                }
            case TypeCode.UInt16:
                {
                    var num = (ushort)flags;
                    return Unsafe.As<ushort, T>(ref num);
                }
            case TypeCode.Int32:
                {
                    var num = (int)flags;
                    return Unsafe.As<int, T>(ref num);
                }
            case TypeCode.UInt32:
                {
                    var num = (uint)flags;
                    return Unsafe.As<uint, T>(ref num);
                }
            case TypeCode.Int64:
                {
                    var num = (long)flags;
                    return Unsafe.As<long, T>(ref num);
                }
            case TypeCode.UInt64:
                {
                    return Unsafe.As<ulong, T>(ref flags);
                }
            default:
                throw new InvalidOperationException($"Unsupported underlying type for enum");
        }
    }
}
