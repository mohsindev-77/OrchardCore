/*
    The two screens that draw a tree of units — DimensionRecordsAdmin/Index, which is a dimension's
    own values, and DimensionRecordsAdmin/Details, which is everything under one value (D24).

    Three behaviours, and all are enhancements of screens that already work without them:

      * collapsing a branch. Without this every row is visible, which is a longer screen and not a
        broken one.
      * Expand all and Collapse all, which are rendered hidden and unhidden here: without the
        script they would be two buttons that do nothing, which is worse than their absence.
      * the Filter button. Without this the filter panel is an open <details> and the button does
        nothing, which is one redundant control and not a hidden filter.

    Nothing here fetches, and nothing here writes. Every control that changes data on these screens
    is a link or a form submit rendered by the server, so a reader with no JavaScript can still
    add, edit, delete, filter, page, switch between tree and list, and open any unit.

    The tree's shape is the server's: each row carries its own record id, its parent's, its depth
    and how many children it has. This never recomputes any of that — it only hides and shows —
    so the script and the server cannot disagree about what the tree is, which is the failure
    ADR-0010's "two readings of one rule" note is about, reached through a different door.

    It binds to EVERY table.record-tree on the page rather than to one id, which is what lets the
    value page reuse it by rendering the same row contract instead of shipping a second copy.
*/
(function () {
    'use strict';

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

    Array.prototype.slice.call(document.querySelectorAll('table.record-tree')).forEach(wireTree);

    function wireTree(table) {
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
    // the control itself, which are doing something else. A tree with no radios in it (the value
    // page's) finds none and does nothing.
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

    /* ---- expand all and collapse all ------------------------------------------------- */

    // Scoped to the controls that name this table, so a page with two trees could have a pair
    // each. The group is rendered hidden by the server and unhidden here: two buttons that do
    // nothing is a worse screen than two buttons that are not there.
    var controls = document.getElementById(table.getAttribute('data-tree-controls') || '');

    if (!controls) {
        return;
    }

    controls.hidden = false;

    controls.addEventListener('click', function (event) {
        var button = event.target.closest ? event.target.closest('button') : null;

        if (!button) {
            return;
        }

        var expanding = button.hasAttribute('data-tree-expand');

        rows.forEach(function (row) {
            // Every row is shown or hidden from the top down, and every control is put into the
            // matching state, so the two cannot disagree afterwards — which is what going through
            // the per-branch functions one row at a time would risk.
            // "Collapsed" means only the rows with nothing above them in THIS table are left,
            // which the server marks by leaving their parent attribute empty. Asked that way
            // rather than by depth, because the two screens number their top row differently —
            // a dimension's own roots are depth 0, a value's children are depth 1 — and a rule
            // that read a number would be right on one screen and wrong on the other.
            row.hidden = expanding ? false : !!row.getAttribute('data-parent-record-id');

            var toggle = row.querySelector('.record-toggle');

            if (toggle) {
                toggle.setAttribute('aria-expanded', expanding ? 'true' : 'false');
                toggle.textContent = expanding ? '−' : '+';
            }
        });
    });
    }

    /* ---- a value's read-only hierarchy (decision D25) ----------------------------------- */

    wireHierarchy();

    function wireHierarchy() {
        var viewport = document.getElementById('value-hierarchy-viewport');
        var canvas = document.getElementById('value-hierarchy-canvas');

        if (!viewport || !canvas) {
            return;
        }

        // ---- expand and collapse -------------------------------------------------------
        //
        // The designer's own chart does this by fetching the branch the first time it opens. This
        // one has the whole subtree already — the page read it in one batched pass — so opening a
        // branch is showing what is there rather than going to get it, and there is no loading
        // state to design and no request to fail.

        function setExpanded(node, expanded) {
            var children = node.querySelector(':scope > .designer-children');
            var toggle = node.querySelector(':scope > .designer-card > .designer-toggle');

            node.setAttribute('data-expanded', expanded ? 'true' : 'false');

            if (children) {
                children.hidden = !expanded;
            }

            if (toggle) {
                toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
            }
        }

        viewport.addEventListener('click', function (event) {
            var toggle = event.target.closest ? event.target.closest('.value-hierarchy-toggle') : null;

            if (!toggle) {
                return;
            }

            var node = toggle.closest('.designer-node');

            if (node) {
                setExpanded(node, node.getAttribute('data-expanded') !== 'true');
            }
        });

        // Expand all and Collapse all, shared with the tree view's controls: the same two buttons
        // mean the same two things, so they are the same pair rather than a second pair that has
        // to be kept in step. Collapsed leaves the root open — collapsing the box the page is
        // about would leave an empty chart.
        var treeControls = document.getElementById('value-tree-controls');

        if (treeControls) {
            treeControls.hidden = false;

            treeControls.addEventListener('click', function (event) {
                var button = event.target.closest ? event.target.closest('button') : null;

                if (!button) {
                    return;
                }

                var expanding = button.hasAttribute('data-tree-expand');

                Array.prototype.slice.call(viewport.querySelectorAll('.designer-node')).forEach(function (node) {
                    setExpanded(node, expanding || node.classList.contains('designer-node-structure'));
                });
            });
        }

        // ---- zoom ----------------------------------------------------------------------
        //
        // `zoom`, not `transform: scale`, and for the reason the designer's own comment gives: a
        // transform leaves the layout box at its original size, so the viewport's scrollbars keep
        // describing the unscaled chart and half of a zoomed-in org chart becomes unreachable.
        // Scaling the layout is what keeps the horizontal scroll honest at every level — which is
        // also what makes a wide chart reachable in RTL, where "wide" runs the other way.

        var controls = document.getElementById(viewport.getAttribute('data-zoom-controls') || '');

        if (!controls) {
            return;
        }

        controls.hidden = false;

        function zoom() {
            return parseFloat(canvas.style.getPropertyValue('--designer-zoom')) || 1;
        }

        function setZoom(value) {
            canvas.style.setProperty('--designer-zoom', String(value));
        }

        controls.addEventListener('click', function (event) {
            var button = event.target.closest ? event.target.closest('[data-value-zoom]') : null;

            if (!button) {
                return;
            }

            var action = button.getAttribute('data-value-zoom');

            if (action === 'fit') {
                // Measured unzoomed, because zoom scales the layout box the measurement comes
                // from — asking a zoomed canvas how wide it is and dividing gives a different
                // answer every press.
                setZoom(1);

                var available = viewport.clientWidth;
                var needed = canvas.scrollWidth;

                // Never upward: "fit to screen" on a chart that already fits is a button that
                // magnifies, which is not what its label says.
                setZoom(needed > available && available > 0 ? Math.max(available / needed, 0.2) : 1);

                return;
            }

            var step = action === 'in' ? 0.1 : -0.1;

            setZoom(Math.min(Math.max(zoom() + step, 0.5), 2));
        });
    }

    /* ---- remembering which view the reader last chose (decision D25) -------------------- */

    rememberUnitsView();

    function rememberUnitsView() {
        var views = document.getElementById('value-units-views');

        if (!views) {
            return;
        }

        var current = views.querySelector('[aria-current="true"]');

        // Stored on every visit, so the memory follows what the reader is actually looking at
        // rather than only what they pressed. Wrapped, because storage throws rather than
        // returning null when a browser is in a mode that forbids it, and losing a preference is
        // not worth losing the page over.
        try {
            if (current) {
                window.localStorage.setItem('workmate.unitsView', current.getAttribute('data-units-view'));
            }
        } catch (ignored) {
            return;
        }

        // A URL that names a view is the reader's explicit choice and is never overridden — which
        // is what keeps a link somebody sent, and the back button, meaning what they say.
        if (/[?&]view=/.test(window.location.search)) {
            return;
        }

        var remembered;

        try {
            remembered = window.localStorage.getItem('workmate.unitsView');
        } catch (ignored) {
            return;
        }

        var target = remembered && views.querySelector('[data-units-view="' + remembered + '"]');

        if (!target || target === current) {
            return;
        }

        // replace, not assign: restoring a preference must not put a page the reader never asked
        // for into their history, where Back would land on it and bounce them forward again.
        window.location.replace(target.getAttribute('href'));
    }
})();
