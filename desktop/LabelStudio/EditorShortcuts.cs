using Avalonia.Input;

namespace LabelStudio;

internal static class EditorShortcuts
{
    public static KeyModifiers CommandModifier => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    public static bool HasCommand(KeyModifiers modifiers) => modifiers.HasFlag(CommandModifier);
}
