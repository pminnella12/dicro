# Word Search Puzzle — Implementation Plan

## Context
Building a word search puzzle web app on an ASP.NET Core 8.0 Razor Pages starter template. The app is currently empty (Index page has no content). The goal is to implement all features from `/requirements/wordsearch.txt` plus extra credit interactive solving, without full page reloads (AJAX throughout).

User choices confirmed:
- All 8 directions (horizontal, vertical, diagonal — forward AND reverse)
- Extra credit: interactive solving (click/drag to highlight words, cross off when found)
- No persistence needed — server-side session only

Each milestone ends with a runnable, browser-testable app.

---

## Milestone 1 — Backend Foundation
**Goal:** All backend infrastructure in place. App starts and compiles; Index page still blank.

### Files to create
| File | Purpose |
|---|---|
| `Models/WordSearchState.cs` | Session-stored domain model |
| `Models/PuzzleResponse.cs` | JSON response shape for all AJAX handlers |
| `Services/WordSearchGenerator.cs` | Word placement algorithm (8 directions) + random fill |
| `Services/SessionExtensions.cs` | Typed JSON session get/set helpers |

### Files to modify
| File | Changes |
|---|---|
| `Program.cs` | Add `AddDistributedMemoryCache`, `AddSession`, `UseSession` |
| `Pages/_ViewImports.cshtml` | Add `@using wordsearch.Models` and `@using wordsearch.Services` |

### Data structures

**`WordSearchState`** (stored in session as JSON):
```csharp
public class WordSearchState {
    public int GridSize { get; set; } = 10;
    public List<string> Words { get; set; } = new();
    // Key = word (uppercase). Value = ordered list of [row, col] pairs.
    public Dictionary<string, List<int[]>> WordPlacements { get; set; } = new();
}
```

**`PuzzleResponse`** (returned by all AJAX handlers):
```csharp
public class PuzzleResponse {
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int GridSize { get; set; }
    public string[][]? Grid { get; set; }           // [row][col] single-char strings
    public List<string> Words { get; set; } = new();
    public Dictionary<string, int[][]>? Placements { get; set; } // word -> [[r,c],...]
}
```

### Generation algorithm (`WordSearchGenerator`)

**8 direction vectors:** right, left, down, up, down-right, down-left, up-right, up-left

**`Generate(int gridSize, IEnumerable<string> words)`:**
1. Init `char[gridSize][gridSize]` with `\0`
2. Sort words descending by length (place longest first to reduce failures)
3. For each word, call `TryPlaceWord`:
   - Generate all `(row, col, direction)` combos, shuffle with Fisher-Yates
   - For each candidate: check each cell is `\0` or already has the correct letter (overlaps OK)
   - On success: write letters, record coords; on exhaustion: return failure with offending word
4. Fill remaining `\0` cells with random A-Z
5. Return `GenerateResult { Success, FailedWord, Grid, Placements }`

**`ValidateWord(string word, int gridSize, List<string> existingWords)`:**
- Word length > `gridSize` → reject immediately
- Duplicate word (case-insensitive) → reject

### Verification
- `dotnet run` starts without errors
- `dotnet build` produces no warnings

---

## Milestone 2 — Core Puzzle UI
**Goal:** Fully functional puzzle. User can set grid size, add/remove words, view the generated puzzle, and rebuild.

### Files to create / modify
| File | Changes |
|---|---|
| `Pages/Index.cshtml.cs` | All AJAX handlers + `OnGet` with `InitialPuzzleResponse` |
| `Pages/Index.cshtml` | Full HTML layout + `<script>var initialState = ...</script>` seed |
| `wwwroot/css/site.css` | Grid cell base styles |
| `wwwroot/js/site.js` | AJAX calls + `renderGrid()` + `renderWordList()` |

### AJAX handlers (all return `JsonResult`)

| Handler | Trigger | Action |
|---|---|---|
| `OnPostSetGridAsync([FromBody] SetGridRequest)` | Grid size change | Reset words, regenerate, save session |
| `OnPostAddWordAsync([FromBody] AddWordRequest)` | Add word button | Validate, add to session, regenerate |
| `OnPostRemoveWordAsync([FromBody] RemoveWordRequest)` | Remove word click | Remove from session, regenerate |
| `OnPostRebuildAsync()` | Rebuild button | Same words, new random layout |

**Anti-forgery:** embed token in a hidden input on `Index.cshtml`; include `RequestVerificationToken` header on every AJAX call.

**`OnGet`:** reads session, runs `Generate` if words exist, populates `InitialPuzzleResponse` property. Razor template serializes it to a `<script>` tag so JS can render on first load without an extra round-trip.

### UI layout

```
Bootstrap row
  [col-md-4]                    [col-md-8]
  Grid size: [input] [Set]       <table id="ws-table"> (JS-rendered)
  Word: [input] [Add]
  <ul id="word-list">
  [Rebuild]
  <div id="status-msg">
```

### Base CSS for grid
```css
.ws-cell {
    width: 2rem; height: 2rem;
    text-align: center; vertical-align: middle;
    cursor: default; user-select: none;
    font-weight: bold; font-family: monospace;
    border: 1px solid #dee2e6;
}
```

### JS structure (IIFE in `site.js`)
```
WordSearch = (function($) {
    let currentGrid = [];   // char[][] mirrored client-side
    let placements = {};    // word -> [[r,c],...] from server

    function apiCall(handler, data, callback) { ... }
    function renderGrid(gridData) { ... }     // builds <table> from string[][]
    function renderWordList(words) { ... }    // rebuilds <ul>

    $('#add-word-btn').on('click', ...)
    $('#rebuild-btn').on('click', ...)
    $('#grid-size-btn').on('click', ...)

    function init() { if (initialState) { renderGrid(...); renderWordList(...); } }
    $(init);
})(jQuery);
```

### Verification
- Set grid size 10, add "HELLO" → puzzle renders with HELLO placed somewhere
- Add a 15-letter word to a 10x10 grid → error shown, grid unchanged
- Click "Rebuild" → new layout, no page reload
- Remove a word → grid regenerates without it

---

## Milestone 3 — Interactive Solving (Extra Credit)
**Goal:** User can click and drag across cells to find words. Found words are highlighted and crossed off. Completing the puzzle shows a congratulations banner.

### Files to modify
| File | Changes |
|---|---|
| `wwwroot/js/site.js` | Selection logic: mousedown/move/up, `checkSelection()`, `markWordFound()` |
| `wwwroot/css/site.css` | Selection highlight, found-cell, found-word, congratulations styles |

### CSS additions
```css
.ws-cell { cursor: pointer; }   /* override Milestone 2 default */

.ws-cell.ws-selected {
    background-color: #cfe2ff;  /* Bootstrap blue-100 */
}
.ws-cell.ws-found {
    background-color: #d1e7dd;  /* Bootstrap green-100 */
}
#word-list li.word-found {
    text-decoration: line-through;
    color: #6c757d;
    transition: color 0.3s;
}
```

### Selection flow
1. `mousedown` on `.ws-cell` → record `selectionStart = {row, col}`, set `isSelecting = true`
2. `mousemove` over `.ws-cell` → compute direction from start to current cell (must be one of 8 valid directions); highlight all cells along that straight line, clear others
3. `mouseup` anywhere → call `checkSelection()`, set `isSelecting = false`, clear highlights after 200ms

### `checkSelection()`
1. Collect ordered `[row, col]` of highlighted cells
2. Build string from `currentGrid`
3. Compare string AND its reverse against every word key in `placements`
4. On match → `markWordFound(word)`

### `markWordFound(word)`
1. Add `ws-found` to each `[row, col]` cell in `placements[word]`
2. Add `word-found` class to `<li data-word="WORD">` in `#word-list`
3. If all words found → show `#congrats-banner`

### Verification
- Drag across HELLO in the grid → cells turn green, "HELLO" crossed off list
- Drag in reverse over a reverse-placed word → still recognized
- Find all words → congratulations banner appears
