namespace DevPilot.Core.Typer;

public enum TypingStepKind { Char, Enter, Tab }

/// <summary>One keystroke. <see cref="Char"/> is a single UTF-16 unit (surrogate halves are sent one after the other).</summary>
public readonly record struct TypingStep(TypingStepKind Kind, char Char = '\0');
