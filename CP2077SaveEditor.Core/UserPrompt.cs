namespace CP2077SaveEditor;

/// <summary>Lets editing logic inform or ask the user without depending on a UI framework.</summary>
public interface IUserPrompt
{
    void Notice(string message, string title);

    /// <returns>True if the user confirmed.</returns>
    bool Confirm(string message, string title);
}

public static class UserPrompt
{
    /// <summary>Set by the front end at startup.</summary>
    public static IUserPrompt Current { get; set; } = new NullUserPrompt();

    public static void Notice(string message, string title = "Notice") => Current.Notice(message, title);
    public static bool Confirm(string message, string title = "Warning") => Current.Confirm(message, title);

    private sealed class NullUserPrompt : IUserPrompt
    {
        public void Notice(string message, string title) { }
        public bool Confirm(string message, string title) => false;
    }
}
