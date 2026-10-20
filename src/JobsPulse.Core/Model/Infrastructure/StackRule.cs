namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>How `FilterSpec.DescriptionAnyOf` is read - see `StackDetector`.</summary>
public enum StackRule
{
    /// <summary>Plain «any of these words in the text».</summary>
    None = 0,

    /// <summary>The stack is one of the main ones: in the title, or in a requirement that is not a nice-to-have.</summary>
    Primary = 1,

    /// <summary>The stack is not a main one, and no production experience with another language is required.</summary>
    Neutral = 2
}
