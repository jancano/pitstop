using System.Text.RegularExpressions;

namespace Pitstop.Modules;

public static partial class ModuleCreation
{
    private static readonly HashSet<string> Reserved = new(("alignas alignof and and_eq asm auto bitand bitor bool break case catch char char8_t char16_t char32_t class "
        + "compl concept const consteval constexpr constinit const_cast continue co_await co_return co_yield decltype default delete do double dynamic_cast else enum explicit "
        + "export extern false float for friend goto if import inline int long module mutable namespace new noexcept not not_eq nullptr operator or or_eq private protected public "
        + "register reinterpret_cast requires return short signed sizeof static static_assert static_cast struct switch template this thread_local throw true try typedef typeid "
        + "typename union unsigned using virtual void volatile wchar_t while xor xor_eq reflexpr contract_assert").Split(' '), StringComparer.Ordinal);

    public static bool IsName(string name) => Identifier().IsMatch(name) && name.Split('.').All(part => !Reserved.Contains(part) && !part.Contains("__", StringComparison.Ordinal));

    public static string Source(string name, string partition, ModuleUnitKind kind, bool importStandardLibrary)
    {
        if (!IsName(name) || name is "std" or "std.compat" || name.StartsWith("std.", StringComparison.Ordinal)
            || (kind is ModuleUnitKind.InterfacePartition or ModuleUnitKind.InternalPartition && !IsName(partition)))
        {
            throw new ArgumentException("Choose a valid C++ module and partition name.");
        }
        string declaration = kind switch
        {
            ModuleUnitKind.PrimaryInterface => $"export module {name};",
            ModuleUnitKind.InterfacePartition => $"export module {name}:{partition};",
            ModuleUnitKind.InternalPartition => $"module {name}:{partition};",
            ModuleUnitKind.Implementation => $"module {name};",
            _ => throw new ArgumentException("Choose a module unit kind."),
        };
        return declaration + (importStandardLibrary ? "\n\nimport std;\n" : "\n");
    }

    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)*\z")]
    private static partial Regex Identifier();
}
