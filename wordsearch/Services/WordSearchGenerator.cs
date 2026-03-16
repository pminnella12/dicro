namespace wordsearch.Services;

/// <summary>
/// Returned by <see cref="WordSearchGenerator.Generate"/> to describe the outcome of a generation attempt.
/// </summary>
/// <param name="Success">True when every word was placed successfully; false if any word could not fit.</param>
/// <param name="FailedWord">The first word that could not be placed, or null when <paramref name="Success"/> is true.</param>
/// <param name="Grid">The completed (or partially completed) letter grid, indexed as [row][column].</param>
/// <param name="Placements">
///   Maps each successfully placed word to the ordered list of [row, col] coordinates it occupies.
/// </param>
public record GenerateResult(
    bool Success,
    string? FailedWord,
    char[][] Grid,
    Dictionary<string, List<int[]>> Placements);

public static class WordSearchGenerator
{
    /// <summary>
    /// All eight directions a word can travel through the grid.
    /// Each tuple holds (rowDelta, columnDelta) — the amount added to the current
    /// row and column index after each letter is placed.
    /// </summary>
    private static readonly (int rowDelta, int columnDelta)[] Directions =
    {
        ( 0,  1),  // right
        ( 0, -1),  // left
        ( 1,  0),  // down
        (-1,  0),  // up
        ( 1,  1),  // down-right
        ( 1, -1),  // down-left
        (-1,  1),  // up-right
        (-1, -1),  // up-left
    };

    /// <summary>
    /// Checks whether a candidate word is acceptable before it is added to the puzzle.
    /// Returns a human-readable error message if validation fails, or null when the word is valid.
    /// </summary>
    /// <param name="word">The raw user-supplied word (may have leading/trailing whitespace).</param>
    /// <param name="gridSize">The side length of the square grid the word must fit within.</param>
    /// <param name="existingWords">Words already added to the puzzle; used to detect duplicates.</param>
    public static string? ValidateWord(string word, int gridSize, List<string> existingWords)
    {
        if (string.IsNullOrWhiteSpace(word))
            return "Word cannot be empty.";

        // Normalize: strip surrounding whitespace and convert to uppercase so all
        // comparisons throughout the system are case-insensitive by default.
        var normalizedWord = word.Trim().ToUpperInvariant();

        if (!normalizedWord.All(char.IsLetter))
            return "Word must contain only letters.";

        // A word longer than the grid side length can never fit in any direction.
        if (normalizedWord.Length > gridSize)
            return $"Word is too long for a {gridSize}x{gridSize} grid.";

        // Prevent the same word from appearing in the puzzle twice.
        if (existingWords.Any(existingWord => existingWord.Equals(normalizedWord, StringComparison.OrdinalIgnoreCase)))
            return "Word is already in the list.";

        return null;
    }

    /// <summary>
    /// Builds a complete word-search grid by placing all supplied words and then filling
    /// every remaining empty cell with a random uppercase letter.
    /// </summary>
    /// <param name="gridSize">Side length of the square grid (e.g. 10 produces a 10×10 grid).</param>
    /// <param name="words">The words to hide in the puzzle.</param>
    /// <param name="rng">
    ///   Optional random-number generator. Pass a seeded instance to get reproducible puzzles;
    ///   leave null to use a default instance with an unpredictable seed.
    /// </param>
    /// <returns>
    ///   A <see cref="GenerateResult"/> describing whether placement succeeded and containing
    ///   the finished grid along with each word's cell coordinates.
    /// </returns>
    public static GenerateResult Generate(int gridSize, IEnumerable<string> words, Random? rng = null)
    {
        rng ??= new Random();

        // Allocate a square grid of null characters ('\0') to represent empty cells.
        var grid = new char[gridSize][];
        for (int rowIndex = 0; rowIndex < gridSize; rowIndex++)
            grid[rowIndex] = new char[gridSize];

        // Tracks the [row, col] coordinates of every letter for each successfully placed word.
        var wordPlacements = new Dictionary<string, List<int[]>>(StringComparer.OrdinalIgnoreCase);

        // Sort words longest-first so that the most constrained words are placed while the
        // grid is still mostly empty, maximizing the chance that every word fits.
        var sortedWords = words
            .Select(word => word.ToUpperInvariant())
            .OrderByDescending(word => word.Length)
            .ToList();

        foreach (var currentWord in sortedWords)
        {
            // Attempt to find a valid position and direction for this word.
            if (!TryPlaceWord(grid, currentWord, gridSize, rng, out var placedCoordinates))
            {
                // Return early with failure details so the caller can report which word blocked generation.
                return new GenerateResult(false, currentWord, grid, wordPlacements);
            }

            wordPlacements[currentWord] = placedCoordinates;
        }

        // Fill every cell that was not claimed by a word with a random letter so the
        // grid looks like a genuine word-search puzzle rather than a sparse letter collection.
        for (int rowIndex = 0; rowIndex < gridSize; rowIndex++)
            for (int columnIndex = 0; columnIndex < gridSize; columnIndex++)
                if (grid[rowIndex][columnIndex] == '\0')
                    grid[rowIndex][columnIndex] = (char)('A' + rng.Next(26));

        return new GenerateResult(true, null, grid, wordPlacements);
    }

    /// <summary>
    /// Attempts to place a single word somewhere in the grid.
    /// Generates every possible (startRow, startColumn, direction) combination, shuffles them
    /// randomly, then tries each one until a conflict-free placement is found.
    /// </summary>
    /// <param name="grid">The live grid being built; modified in-place when a placement succeeds.</param>
    /// <param name="word">The uppercase word to place.</param>
    /// <param name="gridSize">Side length of the grid.</param>
    /// <param name="rng">Random-number generator used to shuffle placement candidates.</param>
    /// <param name="placedCoordinates">
    ///   When this method returns true, contains the ordered [row, col] pairs for each letter.
    ///   Empty when the method returns false.
    /// </param>
    /// <returns>True if the word was placed; false if no valid position exists.</returns>
    private static bool TryPlaceWord(
        char[][] grid,
        string word,
        int gridSize,
        Random rng,
        out List<int[]> placedCoordinates)
    {
        placedCoordinates = new List<int[]>();

        // Pre-allocate a list of every (row, column, directionIndex) triple so we can
        // iterate over all possible starting points and headings in a single shuffled pass.
        var allCandidatePositions = new List<(int startRow, int startColumn, int directionIndex)>(
            gridSize * gridSize * Directions.Length);

        for (int row = 0; row < gridSize; row++)
            for (int column = 0; column < gridSize; column++)
                for (int directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
                    allCandidatePositions.Add((row, column, directionIndex));

        // Shuffle so the puzzle layout is non-deterministic (unless a seeded rng was passed in).
        Shuffle(allCandidatePositions, rng);

        foreach (var (startRow, startColumn, directionIndex) in allCandidatePositions)
        {
            var (rowDelta, columnDelta) = Directions[directionIndex];

            // Track the cells this candidate placement would occupy so we can commit them
            // all at once only after confirming none of them conflict.
            var candidateCells = new List<int[]>(word.Length);
            bool placementFits = true;

            for (int letterIndex = 0; letterIndex < word.Length; letterIndex++)
            {
                // Compute the grid position of this letter given the starting cell and direction.
                int targetRow    = startRow    + rowDelta    * letterIndex;
                int targetColumn = startColumn + columnDelta * letterIndex;

                // Reject the candidate if any letter would land outside the grid boundaries.
                if (targetRow < 0 || targetRow >= gridSize || targetColumn < 0 || targetColumn >= gridSize)
                {
                    placementFits = false;
                    break;
                }

                char existingCellValue = grid[targetRow][targetColumn];

                // A cell is usable if it is still empty ('\0') or already holds the same letter
                // (allowing two crossing words to share a common letter).
                if (existingCellValue != '\0' && existingCellValue != word[letterIndex])
                {
                    placementFits = false;
                    break;
                }

                candidateCells.Add(new[] { targetRow, targetColumn });
            }

            if (!placementFits) continue;

            // All cells are conflict-free — write the word's letters into the grid.
            for (int letterIndex = 0; letterIndex < word.Length; letterIndex++)
                grid[candidateCells[letterIndex][0]][candidateCells[letterIndex][1]] = word[letterIndex];

            placedCoordinates = candidateCells;
            return true;
        }

        // Exhausted all positions without finding a valid placement.
        return false;
    }

    /// <summary>
    /// Performs an in-place Fisher-Yates shuffle on <paramref name="list"/>,
    /// producing a uniformly random permutation using <paramref name="rng"/>.
    /// </summary>
    private static void Shuffle<T>(List<T> list, Random rng)
    {
        // Iterate from the last element down to index 1, swapping each element with
        // a randomly chosen element at or before its current position.
        for (int currentIndex = list.Count - 1; currentIndex > 0; currentIndex--)
        {
            int swapIndex = rng.Next(currentIndex + 1);
            (list[currentIndex], list[swapIndex]) = (list[swapIndex], list[currentIndex]);
        }
    }
}
