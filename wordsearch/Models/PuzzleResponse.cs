namespace wordsearch.Models;

public class PuzzleResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int GridSize { get; set; }
    public string[][]? Grid { get; set; }
    public List<string> Words { get; set; } = new();
    // word -> ordered [[row, col], ...] pairs
    public Dictionary<string, int[][]>? Placements { get; set; }
}

public record SetGridRequest(int Size);
public record AddWordRequest(string Word);
public record RemoveWordRequest(string Word);
