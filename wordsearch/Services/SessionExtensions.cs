using System.Text.Json;
using wordsearch.Models;

namespace wordsearch.Services;

public static class SessionExtensions
{
    private const string StateKey = "WordSearchState";

    public static WordSearchState GetPuzzleState(this ISession session)
    {
        var json = session.GetString(StateKey);
        return json is null
            ? new WordSearchState()
            : JsonSerializer.Deserialize<WordSearchState>(json)!;
    }

    public static void SetPuzzleState(this ISession session, WordSearchState state)
    {
        session.SetString(StateKey, JsonSerializer.Serialize(state));
    }
}
