using System.Reflection;

namespace Kolvir.Docs.Reference;

/// <summary>
/// Builds the member ids the compiler writes into the XML documentation file.
/// </summary>
/// <remarks>
/// The ids are not a convenience: they are how a reflected member finds its prose. A mismatch is
/// silent - the member renders with no summary - so the generator reports every miss instead of
/// letting one through.
/// </remarks>
internal static class DocIds
{
    /// <summary>The id of a type's documentation entry.</summary>
    /// <param name="type">The type.</param>
    /// <returns>An id such as <c>T:Kolvir.Button</c>.</returns>
    internal static string ForType(Type type) => "T:" + DeclaringName(type);

    /// <summary>The id of a constructor's or method's documentation entry.</summary>
    /// <param name="method">The method.</param>
    /// <returns>An id such as <c>M:Kolvir.Button.#ctor(System.String)</c>.</returns>
    internal static string ForMethod(MethodBase method)
    {
        string name = method is ConstructorInfo ? "#ctor" : method.Name;

        if (method.IsGenericMethodDefinition)
        {
            name += "``" + method.GetGenericArguments().Length;
        }

        string parameters = string.Join(',', method.GetParameters().Select(parameter => ParameterName(parameter.ParameterType)));
        string owner = DeclaringName(method.DeclaringType!);

        return parameters.Length == 0 ? $"M:{owner}.{name}" : $"M:{owner}.{name}({parameters})";
    }

    /// <summary>The id of a property's documentation entry, including an indexer's parameters.</summary>
    /// <param name="property">The property.</param>
    /// <returns>An id such as <c>P:Kolvir.Button.Text</c>.</returns>
    internal static string ForProperty(PropertyInfo property)
    {
        string owner = DeclaringName(property.DeclaringType!);
        var indexParameters = property.GetIndexParameters();

        if (indexParameters.Length == 0)
        {
            return $"P:{owner}.{property.Name}";
        }

        return $"P:{owner}.{property.Name}({string.Join(',', indexParameters.Select(parameter => ParameterName(parameter.ParameterType)))})";
    }

    /// <summary>The id of a field's documentation entry.</summary>
    /// <param name="field">The field.</param>
    /// <returns>An id such as <c>F:Kolvir.Theme.Background</c>.</returns>
    internal static string ForField(FieldInfo field) => $"F:{DeclaringName(field.DeclaringType!)}.{field.Name}";

    /// <summary>Turns a cref into the name a page shows.</summary>
    /// <param name="cref">The id a <c>cref</c> attribute carries.</param>
    /// <returns><c>Button</c> for a type, <c>Control.Text</c> for a member, or the cref itself.</returns>
    internal static string DisplayName(string cref)
    {
        if (cref.Length < 3 || cref[1] != ':')
        {
            return cref;
        }

        char kind = cref[0];
        string body = cref[2..];

        int parenthesis = body.IndexOf('(', StringComparison.Ordinal);
        if (parenthesis >= 0)
        {
            body = body[..parenthesis];
        }

        int brace = body.IndexOf('{', StringComparison.Ordinal);
        if (brace >= 0)
        {
            body = body[..brace];
        }

        var parts = new List<string>();
        foreach (string part in body.Split('.'))
        {
            int tick = part.IndexOf('`', StringComparison.Ordinal);
            parts.Add(tick >= 0 ? part[..tick] : part);
        }

        if (parts.Count == 0)
        {
            return body;
        }

        if (kind == 'T')
        {
            return parts[^1];
        }

        return parts.Count >= 2 ? parts[^2] + "." + parts[^1] : parts[^1];
    }

    /// <summary>The type name in the form a documentation id uses: namespace-qualified, nested with a dot.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The name, with arity kept for a generic definition.</returns>
    internal static string DeclaringName(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    private static string ParameterName(Type type)
    {
        if (type.IsByRef)
        {
            return ParameterName(type.GetElementType()!) + "@";
        }

        if (type.IsPointer)
        {
            return ParameterName(type.GetElementType()!) + "*";
        }

        if (type.IsGenericParameter)
        {
            return "`" + type.GenericParameterPosition;
        }

        if (type.IsConstructedGenericType)
        {
            string definition = DeclaringName(type.GetGenericTypeDefinition());
            int tick = definition.IndexOf('`', StringComparison.Ordinal);
            if (tick >= 0)
            {
                definition = definition[..tick];
            }

            return definition + "{" + string.Join(',', type.GetGenericArguments().Select(ParameterName)) + "}";
        }

        return DeclaringName(type);
    }
}
