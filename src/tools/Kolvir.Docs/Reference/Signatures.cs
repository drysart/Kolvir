using System.Globalization;
using System.Reflection;

namespace Kolvir.Docs.Reference;

/// <summary>
/// Writes a member's declaration the way the source writes it, for a page to display.
/// </summary>
internal static class Signatures
{
    private static readonly Dictionary<Type, string> Aliases = new()
    {
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(sbyte)] = "sbyte",
        [typeof(char)] = "char",
        [typeof(decimal)] = "decimal",
        [typeof(double)] = "double",
        [typeof(float)] = "float",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(object)] = "object",
        [typeof(string)] = "string",
        [typeof(void)] = "void",
    };

    /// <summary>A constructor's declaration.</summary>
    /// <param name="constructor">The constructor.</param>
    /// <returns>Such as <c>public Button(string text)</c>.</returns>
    internal static string ForConstructor(ConstructorInfo constructor) =>
        $"{Access(constructor)} {Short(constructor.DeclaringType!)}" + Parameters(constructor.GetParameters());

    /// <summary>A property's declaration.</summary>
    /// <param name="property">The property.</param>
    /// <returns>Such as <c>public string Text { get; set; }</c>.</returns>
    internal static string ForProperty(PropertyInfo property)
    {
        var accessors = new List<string>();
        if (property.GetMethod is { IsPublic: true })
        {
            accessors.Add("get;");
        }

        if (property.SetMethod is { IsPublic: true } setter)
        {
            accessors.Add(IsInitOnly(setter) ? "init;" : "set;");
        }

        var indexParameters = property.GetIndexParameters();
        string name = indexParameters.Length == 0
            ? property.Name
            : $"this[{string.Join(", ", indexParameters.Select(ForParameter))}]";

        string modifiers = property.GetMethod?.IsStatic == true || property.SetMethod?.IsStatic == true
            ? "public static "
            : "public ";

        return $"{modifiers}{Short(property.PropertyType)} {name} {{ {string.Join(' ', accessors)} }}";
    }

    /// <summary>A method's declaration.</summary>
    /// <param name="method">The method.</param>
    /// <returns>Such as <c>public IDisposable AddClickHandler(Action&lt;object&gt; handler)</c>.</returns>
    internal static string ForMethod(MethodInfo method)
    {
        string generic = method.IsGenericMethodDefinition
            ? "<" + string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name)) + ">"
            : string.Empty;

        string modifiers = Access(method) + (method.IsStatic ? " static" : string.Empty);

        return $"{modifiers} {Short(method.ReturnType)} {method.Name}{generic}{Parameters(method.GetParameters())}";
    }

    /// <summary>A field's declaration.</summary>
    /// <param name="field">The field.</param>
    /// <returns>Such as <c>public static readonly Theme Light</c>.</returns>
    internal static string ForField(FieldInfo field) =>
        $"public {(field.IsStatic ? "static " : string.Empty)}{(field.IsInitOnly ? "readonly " : string.Empty)}"
        + $"{Short(field.FieldType)} {field.Name}";

    /// <summary>A type's name as it is written in code.</summary>
    /// <param name="type">The type.</param>
    /// <returns>Such as <c>IReadOnlyList&lt;string&gt;</c>.</returns>
    internal static string Short(Type type)
    {
        if (Aliases.TryGetValue(type, out string? alias))
        {
            return alias;
        }

        if (type.IsByRef)
        {
            return Short(type.GetElementType()!);
        }

        if (type.IsArray)
        {
            return Short(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Short(underlying) + "?";
        }

        if (type.IsConstructedGenericType)
        {
            string name = type.Name;
            int tick = name.IndexOf('`', StringComparison.Ordinal);
            if (tick >= 0)
            {
                name = name[..tick];
            }

            return name + "<" + string.Join(", ", type.GetGenericArguments().Select(Short)) + ">";
        }

        if (type.IsNested && type.DeclaringType is { } declaring)
        {
            return Short(declaring) + "." + type.Name;
        }

        return type.Name;
    }

    private static string Parameters(ParameterInfo[] parameters) =>
        "(" + string.Join(", ", parameters.Select(ForParameter)) + ")";

    private static string ForParameter(ParameterInfo parameter)
    {
        string modifier = string.Empty;

        if (parameter.IsOut || (parameter.ParameterType.IsByRef && parameter.IsIn))
        {
            modifier = "out ";
        }
        else if (parameter.ParameterType.IsByRef)
        {
            modifier = parameter.IsIn ? "in " : "ref ";
        }
        else if (parameter.GetCustomAttribute<ParamArrayAttribute>() is not null)
        {
            modifier = "params ";
        }

        string declaration = modifier + Short(parameter.ParameterType) + " " + SafeName(parameter);

        if (parameter.HasDefaultValue && parameter.DefaultValue is not null)
        {
            declaration += " = " + Default(parameter.DefaultValue);
        }

        return declaration;
    }

    private static string Default(object value) => value switch
    {
        string text => "\"" + text + "\"",
        bool boolean => boolean ? "true" : "false",
        char character => "'" + character + "'",
        float single => single.ToString("R", CultureInfo.InvariantCulture) + "f",
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        null => "null",
        _ when value.GetType().IsEnum => Short(value.GetType()) + "." + value,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "default",
    };

    private static string SafeName(ParameterInfo parameter)
    {
        string name = parameter.Name ?? "value";

        return name is "in" or "out" or "ref" or "params" or "base" or "this" or "string" or "object"
            or "event" or "operator" or "default" or "null" or "lock" or "fixed" or "switch" or "class"
            ? "@" + name
            : name;
    }

    private static string Access(MethodBase method) =>
        method.IsPublic ? "public" : method.IsFamily ? "protected" : "internal";

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers()
            .Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");
}
