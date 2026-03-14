using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using wordsearch.Models;
using wordsearch.Services;

namespace wordsearch.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;

    public string InitialStateJson { get; private set; } = "null";

    public IndexModel(ILogger<IndexModel> logger)
    {
        _logger = logger;
    }

    public void OnGet()
    {
        var state = HttpContext.Session.GetPuzzleState();
        var response = BuildResponse(state, true, null);
        InitialStateJson = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    public async Task<IActionResult> OnPostSetGridAsync([FromBody] SetGridRequest req)
    {
        if (req.Size < 5 || req.Size > 30)
            return new JsonResult(new PuzzleResponse { Success = false, Error = "Grid size must be between 5 and 30." });

        var state = new WordSearchState { GridSize = req.Size };
        HttpContext.Session.SetPuzzleState(state);

        await Task.CompletedTask;
        return new JsonResult(BuildResponse(state, true, null), CamelCaseOptions());
    }

    public async Task<IActionResult> OnPostAddWordAsync([FromBody] AddWordRequest req)
    {
        var state = HttpContext.Session.GetPuzzleState();
        var word = (req.Word ?? "").Trim().ToUpperInvariant();

        var validationError = WordSearchGenerator.ValidateWord(word, state.GridSize, state.Words);
        if (validationError != null)
            return new JsonResult(new PuzzleResponse { Success = false, Error = validationError });

        state.Words.Add(word);

        var result = WordSearchGenerator.Generate(state.GridSize, state.Words);
        if (!result.Success)
        {
            state.Words.Remove(word);
            return new JsonResult(new PuzzleResponse
            {
                Success = false,
                Error = $"Grid is too full to place \"{word}\". Try a larger grid or remove some words."
            });
        }

        state.WordPlacements = result.Placements;
        HttpContext.Session.SetPuzzleState(state);

        await Task.CompletedTask;
        return new JsonResult(BuildResponse(state, true, result), CamelCaseOptions());
    }

    public async Task<IActionResult> OnPostRemoveWordAsync([FromBody] RemoveWordRequest req)
    {
        var state = HttpContext.Session.GetPuzzleState();
        var word = (req.Word ?? "").Trim().ToUpperInvariant();

        state.Words.RemoveAll(w => w.Equals(word, StringComparison.OrdinalIgnoreCase));

        GenerateResult? result = null;
        if (state.Words.Count > 0)
        {
            result = WordSearchGenerator.Generate(state.GridSize, state.Words);
            if (result.Success)
                state.WordPlacements = result.Placements;
        }
        else
        {
            state.WordPlacements = new Dictionary<string, List<int[]>>();
        }

        HttpContext.Session.SetPuzzleState(state);

        await Task.CompletedTask;
        return new JsonResult(BuildResponse(state, true, result), CamelCaseOptions());
    }

    public async Task<IActionResult> OnPostRebuildAsync()
    {
        var state = HttpContext.Session.GetPuzzleState();

        GenerateResult? result = null;
        if (state.Words.Count > 0)
        {
            result = WordSearchGenerator.Generate(state.GridSize, state.Words);
            if (result.Success)
                state.WordPlacements = result.Placements;
        }

        HttpContext.Session.SetPuzzleState(state);

        await Task.CompletedTask;
        return new JsonResult(BuildResponse(state, true, result), CamelCaseOptions());
    }

    private static PuzzleResponse BuildResponse(WordSearchState state, bool success, GenerateResult? result)
    {
        string[][]? grid = null;
        Dictionary<string, int[][]>? placements = null;

        if (result?.Success == true)
        {
            grid = result.Grid.Select(row => row.Select(c => c.ToString()).ToArray()).ToArray();
            placements = result.Placements.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(coord => coord).ToArray()
            );
        }
        else if (result == null && state.Words.Count == 0)
        {
            // No words yet — return empty grid shell
        }

        return new PuzzleResponse
        {
            Success = success,
            GridSize = state.GridSize,
            Grid = grid,
            Words = state.Words.ToList(),
            Placements = placements
        };
    }

    private static JsonSerializerOptions CamelCaseOptions() =>
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
