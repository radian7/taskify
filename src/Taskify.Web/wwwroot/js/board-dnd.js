// Small helper for dragging task cards between board columns (research R6). The board itself is plain Blazor:
// Blazor handles "dragstart" (which card) and "drop" (which column). This file only does the two things that
// must happen synchronously in the browser and would otherwise flood the server with events or not work at all:
//   1. Firefox starts a drag only if the dragstart event puts some data on the drag.
//   2. A drop is allowed only if "dragover" is cancelled, and "dragover" fires many times a second.
// It is served from the same origin, so the Content Security Policy ("script-src 'self'") allows it.
(function () {
    'use strict';

    document.addEventListener('dragstart', function (event) {
        var card = event.target instanceof Element ? event.target.closest('[data-task-id]') : null;
        if (card && event.dataTransfer) {
            event.dataTransfer.setData('text/plain', card.getAttribute('data-task-id') || '');
            event.dataTransfer.effectAllowed = 'move';
        }
    });

    document.addEventListener('dragover', function (event) {
        if (event.target instanceof Element && event.target.closest('.board-column')) {
            event.preventDefault();
            event.dataTransfer.dropEffect = 'move';
        }
    });

    document.addEventListener('dragenter', function (event) {
        var column = event.target instanceof Element ? event.target.closest('.board-column') : null;
        if (column) {
            column.classList.add('drop-target');
        }
    });

    document.addEventListener('dragleave', function (event) {
        var column = event.target instanceof Element ? event.target.closest('.board-column') : null;
        if (column && !column.contains(event.relatedTarget)) {
            column.classList.remove('drop-target');
        }
    });

    document.addEventListener('dragend', clearHighlights);
    document.addEventListener('drop', clearHighlights);

    function clearHighlights() {
        document.querySelectorAll('.board-column.drop-target').forEach(function (column) {
            column.classList.remove('drop-target');
        });
    }
})();
