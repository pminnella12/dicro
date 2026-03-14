// Dark mode
(function () {
    var stored = localStorage.getItem('theme');
    if (stored === 'dark') {
        document.documentElement.setAttribute('data-bs-theme', 'dark');
    }
    document.addEventListener('DOMContentLoaded', function () {
        var btn = document.getElementById('dark-mode-toggle');
        if (!btn) return;
        function update() {
            var isDark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
            btn.textContent = isDark ? '\u2600' : '\u263E';
        }
        update();
        btn.addEventListener('click', function () {
            var isDark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
            if (isDark) {
                document.documentElement.removeAttribute('data-bs-theme');
                localStorage.setItem('theme', 'light');
            } else {
                document.documentElement.setAttribute('data-bs-theme', 'dark');
                localStorage.setItem('theme', 'dark');
            }
            update();
        });
    });
})();

var WordSearch = (function ($) {
    // --- State ---
    var currentGrid = [];     // string[][] mirrored client-side
    var placements = {};      // word -> [[r,c], ...]
    var foundWords = new Set();
    var isSelecting = false;
    var selectionStart = null;
    var selectionDir = null;  // {dr, dc} locked after first move

    // --- Helpers ---
    function getToken() {
        return $('input[name="__RequestVerificationToken"]').val();
    }

    function apiCall(handler, data, callback) {
        $.ajax({
            url: '/?handler=' + handler,
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(data || {}),
            headers: { 'RequestVerificationToken': getToken() },
            success: function (res) { callback(res); },
            error: function () { showStatus('Request failed. Please try again.', 'danger'); }
        });
    }

    function showStatus(msg, type) {
        var el = $('#status-msg');
        if (!msg) { el.html(''); return; }
        el.html('<div class="alert alert-' + (type || 'danger') + ' alert-sm py-1 px-2 mb-0">' + msg + '</div>');
    }

    // --- Rendering ---
    function renderGrid(gridData) {
        if (!gridData || gridData.length === 0) {
            $('#puzzle-container').html('<p class="text-muted">Set a grid size and add words to generate your puzzle.</p>');
            return;
        }

        currentGrid = gridData;
        var html = '<table id="ws-table">';
        for (var r = 0; r < gridData.length; r++) {
            html += '<tr>';
            for (var c = 0; c < gridData[r].length; c++) {
                html += '<td class="ws-cell" data-row="' + r + '" data-col="' + c + '">' + gridData[r][c] + '</td>';
            }
            html += '</tr>';
        }
        html += '</table>';
        $('#puzzle-container').html(html);

        // Re-apply found highlights
        foundWords.forEach(function (word) {
            if (placements[word]) {
                placements[word].forEach(function (coord) {
                    getCell(coord[0], coord[1]).addClass('ws-found');
                });
            }
        });
    }

    function renderWordList(words) {
        if (!words || words.length === 0) {
            $('#word-list').html('');
            $('#rebuild-btn').hide();
            return;
        }

        var html = '';
        words.forEach(function (w) {
            var foundClass = foundWords.has(w) ? ' word-found' : '';
            html += '<li class="' + foundClass + '" data-word="' + w + '">'
                + '<button class="remove-word" data-word="' + w + '" title="Remove">&times;</button>'
                + '<span>' + w + '</span>'
                + '</li>';
        });
        $('#word-list').html(html);
        $('#rebuild-btn').show();
    }

    function showPuzzleMode() {
        $('#input-controls').hide();
        $('#create-new-btn').show();
    }

    function showEditMode() {
        $('#input-controls').show();
        $('#create-new-btn').hide();
    }

    function applyPuzzleResponse(res) {
        if (!res.success && res.error) {
            showStatus(res.error, 'danger');
            return false;
        }
        showStatus('');
        if (res.placements) placements = res.placements;
        if (res.grid) renderGrid(res.grid);
        renderWordList(res.words);
        return true;
    }

    function getCell(row, col) {
        return $('#ws-table td[data-row="' + row + '"][data-col="' + col + '"]');
    }

    // --- AJAX actions ---
    function onSetGrid() {
        var size = parseInt($('#grid-size-input').val(), 10);
        foundWords.clear();
        $('#congrats-banner').hide();
        apiCall('SetGrid', { size: size }, function (res) {
            applyPuzzleResponse(res);
        });
    }

    function onAddWord() {
        var word = $('#word-input').val().trim();
        if (!word) return;
        apiCall('AddWord', { word: word }, function (res) {
            if (applyPuzzleResponse(res)) {
                $('#word-input').val('');
            }
        });
    }

    function onRemoveWord(word) {
        foundWords.delete(word.toUpperCase());
        apiCall('RemoveWord', { word: word }, function (res) {
            applyPuzzleResponse(res);
        });
    }

    function onRebuild() {
        foundWords.clear();
        $('#congrats-banner').hide();
        apiCall('Rebuild', {}, function (res) {
            if (applyPuzzleResponse(res)) {
                showPuzzleMode();
            }
        });
    }

    function onCreateNew() {
        apiCall('SetGrid', { size: 10 }, function (res) {
            foundWords.clear();
            placements = {};
            currentGrid = [];
            showStatus('');
            $('#congrats-banner').hide();
            $('#grid-size-input').val(10);
            renderGrid(null);
            renderWordList([]);
            showEditMode();
        });
    }

    // --- Interactive solving ---
    function highlightPath(start, end) {
        $('.ws-cell').removeClass('ws-selected');

        var dr = end.row - start.row;
        var dc = end.col - start.col;

        var steps = Math.max(Math.abs(dr), Math.abs(dc));
        if (steps === 0) {
            getCell(start.row, start.col).addClass('ws-selected');
            return;
        }

        var normDr = dr === 0 ? 0 : dr / Math.abs(dr);
        var normDc = dc === 0 ? 0 : dc / Math.abs(dc);

        // Lock direction on first move away from start
        if (!selectionDir) {
            selectionDir = { dr: normDr, dc: normDc };
        }

        var d = selectionDir;
        var stepsAlong = (d.dr !== 0) ? Math.round((end.row - start.row) / d.dr)
                       : (d.dc !== 0) ? Math.round((end.col - start.col) / d.dc) : 0;
        stepsAlong = Math.max(0, stepsAlong);

        for (var i = 0; i <= stepsAlong; i++) {
            var r = start.row + d.dr * i;
            var c = start.col + d.dc * i;
            if (r >= 0 && r < currentGrid.length && c >= 0 && c < currentGrid[0].length) {
                getCell(r, c).addClass('ws-selected');
            }
        }
    }

    function checkSelection() {
        var selected = $('.ws-cell.ws-selected');
        if (selected.length === 0) return;

        var str = '';
        selected.each(function () {
            var r = parseInt($(this).data('row'), 10);
            var c = parseInt($(this).data('col'), 10);
            str += currentGrid[r][c];
        });

        var rev = str.split('').reverse().join('');

        var matched = null;
        Object.keys(placements).forEach(function (word) {
            if (!matched && (word === str || word === rev)) {
                matched = word;
            }
        });

        if (matched) {
            markWordFound(matched);
        }

        setTimeout(function () {
            $('.ws-cell').removeClass('ws-selected');
        }, matched ? 100 : 200);
    }

    function markWordFound(word) {
        if (foundWords.has(word)) return;
        foundWords.add(word);

        if (placements[word]) {
            placements[word].forEach(function (coord) {
                getCell(coord[0], coord[1]).addClass('ws-found');
            });
        }

        $('#word-list li[data-word="' + word + '"]').addClass('word-found');

        var allWords = Object.keys(placements);
        if (allWords.length > 0 && allWords.every(function (w) { return foundWords.has(w); })) {
            setTimeout(function () { $('#congrats-banner').show(); }, 300);
        }
    }

    // --- Event bindings ---
    $('#grid-size-btn').on('click', onSetGrid);
    $('#grid-size-input').on('keydown', function (e) { if (e.key === 'Enter') onSetGrid(); });

    $('#add-word-btn').on('click', onAddWord);
    $('#word-input').on('keydown', function (e) { if (e.key === 'Enter') onAddWord(); });

    $('#rebuild-btn').on('click', onRebuild);
    $('#create-new-btn').on('click', onCreateNew);

    $(document).on('click', '.remove-word', function (e) {
        e.stopPropagation();
        onRemoveWord($(this).data('word'));
    });

    $(document).on('mousedown', '.ws-cell', function (e) {
        e.preventDefault();
        if (currentGrid.length === 0) return;
        isSelecting = true;
        selectionDir = null;
        selectionStart = { row: parseInt($(this).data('row'), 10), col: parseInt($(this).data('col'), 10) };
        highlightPath(selectionStart, selectionStart);
    });

    $(document).on('mousemove', '.ws-cell', function () {
        if (!isSelecting || !selectionStart) return;
        var end = { row: parseInt($(this).data('row'), 10), col: parseInt($(this).data('col'), 10) };
        highlightPath(selectionStart, end);
    });

    $(document).on('mouseup', function () {
        if (!isSelecting) return;
        isSelecting = false;
        if (selectionStart) {
            checkSelection();
        }
        selectionStart = null;
        selectionDir = null;
    });

    // --- Init ---
    function init() {
        if (initialState && initialState.grid) {
            placements = initialState.placements || {};
            renderGrid(initialState.grid);
            renderWordList(initialState.words);
            if (initialState.gridSize) {
                $('#grid-size-input').val(initialState.gridSize);
            }
            showPuzzleMode();
        }
    }

    $(init);

    return {};
})(jQuery);
