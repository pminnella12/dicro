namespace wordsearch.Models;

public class WordSearchState
{
    public int GridSize { get; set; } = 10;
    public List<string> Words { get; set; } = new();
    // Key = word (uppercase). Value = ordered list of [row, col] pairs.
    public Dictionary<string, List<int[]>> WordPlacements { get; set; } = new();
}
