using Discord;

namespace discpywalNET;

public static class Structs
{
    public enum Response
    {
        NoChange,
        Failed,
        Success,
        NoRole,
        FatalFailure
    }

    public struct Activity
    {
        public ActivityType Type { get; init; }
        public string Text { get; init; }
    }
}