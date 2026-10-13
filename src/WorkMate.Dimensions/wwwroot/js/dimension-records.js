/*
    A dimension's own list and tree — DimensionRecordsAdmin/Index.

    Two behaviours, and both are enhancements of a screen that already works without them:

      * collapsing a branch of the tree. Without this every row is visible, which is a longer
        screen and not a broken one.
      * the Filter button. Without this the filter panel is an open <details> and the button does
        nothing, which is one redundant control and not a hidden filter.

    Nothing here fetches, and nothing here writes. Every control that changes data on this screen
    is a link or a form submit rendered by the server, so a reader with no JavaScript can still
    add, edit, delete, filter, page and switch between tree and list.

    The tree's shape is the server's: each row carries its own record id, its parent's, its depth
    and how many children it has. This never recomputes any of that — it only hides and shows —
    so the script and the server cannot disagree about what the tree is, which is the failure
    ADR-0010's "two readings of one rule" note is about, reached through a different door.
*/
(function () {
    'use strict';

    var table = document.getElementById('record-list');
    var panel = document.getElementById('record-filter-panel');
    var filterButton = document.getElementById('record-filter-toggle');

    /* ---- the filter panel ---------------------------------------------------------------- */

    if (panel && filterButton) {
        filterButton.addEventListener('click', function () {
            panel.open = !panel.open;
            filterButton.setAttribute('aria-expanded', panel.open ? 'true' : 'false');
        });

        // The panel can also be opened by the browser's own disclosure behaviour (keyboard, or a
        // user stylesheet), so the button's state follows the element rather than its own memory
        // of what it last did.
        panel.addEventListener('toggle', function () {
            filterButton.setAttribute('aria-expanded', panel.open ? 'true' : 'false');
        });
    }

    /* ---- collapsing a branch ------------------------------------------------------------- */

    if (!table || !table.classList.contains('record-tree')) {
        return;
    }

    var rows = Array.prototype.slice.call(table.querySelectorAll('tr.record-row'));

    // One pass to build "who are this row's children", so toggling is a walk of a map rather than
    // a query of the DOM per click. Keyed by record id, which is what the server put on the row.
    var childrenOf = Object.create(null);

    rows.forEach(function (row) {
        var parentId = row.getAttribute('data-parent-record-id');

        if (!parentId) {
            return;
        }

        (childrenOf[parentId] = childrenOf[parentId] || []).push(row);
    });

    // Every row beneath this one, however deep. Iterative rather than recursive for the reason the
    // server's own flattening is: the depth of a customer's tree is their business.
    function descendantsOf(row) {
        var found = [];
        var pending = [row.getAttribute('data-record-id')];

        while (pending.length > 0) {
            var children = childrenOf[pending.pop()];

            if (!children) {
                continue;
            }

            children.forEach(function (child) {
                found.push(child);
                pending.push(child.getAttribute('data-record-id'));
            });
        }

        return found;
    }

    function collapse(row, button) {
        descendantsOf(row).forEach(function (descendant) {
            descendant.hidden = true;

            // A branch closed inside a branch stays closed when the outer one reopens: its own
            // control still says collapsed, and reopening the outer one asks each control what it
            // thinks rather than overruling it.
            var inner = descendant.querySelector('.record-toggle');

            if (inner) {
                inner.setAttribute('aria-expanded', 'false');
                inner.textContent = '+';
            }
        });

        button.setAttribute('aria-expanded', 'false');
        button.textContent = '+';
    }

    function expand(row, button) {
        // Only the rows this one directly holds. Anything deeper is its own control's business,
        // and reopening a branch must not also reopen the sub-branches somebody closed inside it.
        var children = childrenOf[row.getAttribute('data-record-id')] || [];

        children.forEach(function (child) {
            child.hidden = false;

            var inner = child.querySelector('.record-toggle');

            if (inner && inner.getAttribute('aria-expanded') === 'true') {
                expand(child, inner);
            }
        });

        button.setAttribute('aria-expanded', 'true');
        button.textContent = '−';
    }

    table.addEventListener('click', function (event) {
        var button = event.target.closest ? event.target.closest('.record-toggle') : null;

        if (!button || !table.contains(button)) {
            return;
        }

        var row = button.closest('tr.record-row');

        if (!row) {
            return;
        }

        if (button.getAttribute('aria-expanded') === 'false') {
            expand(row, button);
        } else {
            collapse(row, button);
        }
    });

    // Choosing a row is what the toolbar's Edit and Delete act on, and a radio inside a table cell
    // is a small target. Clicking anywhere on the row selects it — except on a link, a button or
    // the control itself, which are doing something else.
    table.addEventListener('click', function (event) {
        if (event.target.closest('a, button, label, input')) {
            return;
        }

        var row = event.target.closest('tr.record-row');
        var radio = row && row.querySelector('.record-select');

        if (radio) {
            radio.checked = true;
        }
    });
})();
