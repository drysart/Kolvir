// netstandard2.0 predates `init` accessors and therefore the marker type the compiler looks for when
// a record's positional parameters are turned into init-only properties. Declaring it is the
// documented way to use records on this target; it carries no behaviour.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
