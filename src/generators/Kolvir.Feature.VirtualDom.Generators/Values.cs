using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Kolvir.Feature.VirtualDom.Generators;

/// <summary>
/// A declared pristine value, read from <c>[VNodePropertyUnsetValue]</c> and written as C# source.
/// </summary>
/// <remarks>
/// Shared by the native property walk and the extension catalogue, because the attribute is meant to
/// work on either: it is read from the member the generator flattens, wherever that member is
/// declared.
/// </remarks>
internal static class Values
{
    private const string AttributeName = "Kolvir.Feature.VirtualDom.VNodePropertyUnsetValueAttribute";

    /// <summary>The value the attribute declared for <paramref name="property"/>, as a C# literal, or null.</summary>
    internal static string? Unset(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != AttributeName
                || attribute.ConstructorArguments.Length != 1)
            {
                continue;
            }

            return Literal(attribute.ConstructorArguments[0], property.Type);
        }

        return null;
    }

    /// <summary>
    /// A compile-time constant as C# source, or null when the value is not a constant this generator
    /// can write - in which case the node reads it from a constructed control instead, which is always
    /// correct.
    /// </summary>
    private static string? Literal(TypedConstant constant, ITypeSymbol type) =>
        constant.Kind switch
        {
            TypedConstantKind.Primitive => Primitive(constant.Value, type),
            TypedConstantKind.Enum => "(" + type.ToDisplayString(Formats.Type) + ")"
                + Convert.ToString(constant.Value, CultureInfo.InvariantCulture),
            _ => null,
        };

    private static string? Primitive(object? value, ITypeSymbol type)
    {
        if (value is null)
        {
            return type.IsValueType && type.NullableAnnotation == NullableAnnotation.NotAnnotated ? null : "null";
        }

        return value switch
        {
            bool boolean => boolean ? "true" : "false",
            string text => Quote(text),
            char character => Quote(character),
            float single => Single(single),
            double number => Double(number),
            decimal money => money.ToString(CultureInfo.InvariantCulture) + "m",
            sbyte or byte or short or ushort or int => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            uint unsigned => Convert.ToString(unsigned, CultureInfo.InvariantCulture) + "u",
            long wide => Convert.ToString(wide, CultureInfo.InvariantCulture) + "L",
            ulong wide => Convert.ToString(wide, CultureInfo.InvariantCulture) + "UL",
            _ => null,
        };
    }

    private static string Single(float value) => value switch
    {
        float.NaN => "float.NaN",
        float.PositiveInfinity => "float.PositiveInfinity",
        float.NegativeInfinity => "float.NegativeInfinity",
        _ => value.ToString("R", CultureInfo.InvariantCulture) + "f",
    };

    private static string Double(double value) => value switch
    {
        double.NaN => "double.NaN",
        double.PositiveInfinity => "double.PositiveInfinity",
        double.NegativeInfinity => "double.NegativeInfinity",
        _ => value.ToString("R", CultureInfo.InvariantCulture) + "d",
    };

    private static string Quote(string value)
    {
        var builder = new StringBuilder("\"");

        foreach (var character in value)
        {
            builder.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\0' => "\\0",
                _ => character.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }

    private static string Quote(char value) => value switch
    {
        '\\' => "'\\\\'",
        '\'' => "'\\''",
        '\n' => "'\\n'",
        '\r' => "'\\r'",
        '\t' => "'\\t'",
        '\0' => "'\\0'",
        _ => "'" + value + "'",
    };
}
