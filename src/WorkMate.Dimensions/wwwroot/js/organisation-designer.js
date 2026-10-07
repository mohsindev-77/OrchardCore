// Progressive enhancement for the organisation designer: lazily loaded children, switching
// between the chart and the list without a reload, zoom and pan on the chart, and search that
// expands the branch holding a match.
//
// Plain JavaScript, no framework, matching structures.js. Everything here is an enhancement:
// with the script absent the server still renders the chosen view, the roots, the unplaced panel
// and the structure, date and view controls, all of which are plain links and a GET form.
(function () {
    "use strict";

    var surface = document.querySelector(".designer-surface");
    var tree = document.getElementById("designer-tree");
    var nodeTemplate = document.getElementById("designer-node-template");

    if (!surface || !tree || !nodeTemplate) {
        return;
    }

    var structureId = surface.getAttribute("data-structure-id");
    var asAt = surface.getAttribute("data-as-at");
    var childrenUrl = surface.getAttribute("data-children-url");
    var searchUrl = surface.getAttribute("data-search-url");
    var viewCookie = surface.getAttribute("data-view-cookie");
    var employeesLabel = surface.getAttribute("data-employees-label") || "";

    var actionUrls = {
        add: surface.getAttribute("data-add-url"),
        rename: surface.getAttribute("data-rename-url"),
        retire: surface.getAttribute("data-retire-url"),
        move: surface.getAttribute("data-move-url"),
        merge: surface.getAttribute("data-merge-url"),
        cancelmove: surface.getAttribute("data-cancelmove-url")
    };

    var moveUrl = actionUrls.move;

    // Whether this viewer may reparent a unit at all. Without it a card is not draggable, for the
    // same reason it carries no "Move to…" in its menu: the server would refuse either way, and a
    // drag that always ended in a refusal is worse than one that never starts.
    var canMove = surface.getAttribute("data-can-move") === "true";

    var viewport = surface.querySelector(".designer-chart-viewport");
    var canvas = surface.querySelector(".designer-chart-canvas");
    var zoomControls = document.getElementById("designer-zoom-controls");

    function query(parameters) {
        var parts = [];

        Object.keys(parameters).forEach(function (key) {
            if (parameters[key] !== null && parameters[key] !== undefined && parameters[key] !== "") {
                parts.push(encodeURIComponent(key) + "=" + encodeURIComponent(parameters[key]));
            }
        });

        return parts.join("&");
    }

    // ---- the tree, shared by both views -----------------------------------------------

    function findNode(recordId) {
        return tree.querySelector('.designer-node[data-record-id="' + recordId + '"]');
    }

    function buildNode(node) {
        var fragment = nodeTemplate.content.cloneNode(true);
        var li = fragment.querySelector(".designer-node");

        li.setAttribute("data-record-id", node.recordId);
        // One name, the English one, matching what the server renders on a card. The Arabic half
        // is still in the payload — the search results list uses it to show why a hit matched —
        // it just has no line of its own on a card.
        var name = li.querySelector(".designer-card-name");

        name.textContent = node.nameEn;
        name.title = node.nameEn;
        li.querySelector(".designer-card-type").textContent = node.dimensionTypeNameEn;
        li.querySelector(".designer-card-code").textContent = node.code;

        var employees = li.querySelector(".designer-card-employees");
        employees.textContent = node.employeeCount
            ? employeesLabel.replace("{0}", node.employeeCount)
            : "";

        // The same two attributes the server sets when it renders a card: a unit with children
        // gets the expand control showing the count, a unit without gets the spacer instead, so a
        // fetched card is indistinguishable from a server-rendered one.
        var children = node.childCount || 0;

        li.setAttribute("data-child-count", String(children));
        li.querySelector(".designer-toggle").hidden = children === 0;
        li.querySelector(".designer-toggle-count").textContent = String(children);
        li.querySelector(".designer-toggle-spacer").hidden = children > 0;

        // The template's action links were rendered with no record on them, because there was no
        // record to render. Pointed at this one, so a fetched card's menu goes where a
        // server-rendered card's menu goes.
        li.querySelectorAll("[data-designer-action]").forEach(function (link) {
            var action = link.getAttribute("data-designer-action");
            var base = actionUrls[action];

            if (!base) {
                return;
            }

            link.href = base + "?" + query(
                action === "add"
                    ? { structureId: structureId, parentId: node.recordId, asAt: asAt }
                    : { structureId: structureId, recordId: node.recordId, asAt: asAt });
        });

        // A fetched card is draggable exactly as a server-rendered one is: the drag handler reads
        // the DOM rather than a list captured at load, so there is nothing per-card to wire up.
        li.querySelectorAll("[data-designer-action]").forEach(function (link) {
            if (!actionUrls[link.getAttribute("data-designer-action")]) {
                link.remove();
            }
        });

        return li;
    }

    function setExpanded(li, expanded) {
        var children = li.querySelector(".designer-children");
        var toggle = li.querySelector(".designer-toggle");

        children.hidden = !expanded;
        li.setAttribute("data-expanded", expanded ? "true" : "false");
        toggle.setAttribute("aria-expanded", expanded ? "true" : "false");

        levelRows();
    }

    // Every card at one depth grows to the height of the tallest card at that depth, so a row
    // reads as a row even though one card in it has a name that wrapped onto three lines.
    //
    // This cannot be done in CSS. The cards in a row are not siblings — each belongs to its own
    // parent's list — so nothing in the cascade can see across them to the tallest, and the
    // alternative of giving every card one fixed height is what cut the names off in the first
    // place. Measuring is a progressive enhancement: without it the stylesheet's min-block-size
    // keeps rows close to level and nothing is hidden either way.
    function levelRows() {
        if (!isChart()) {
            return;
        }

        var byDepth = {};

        tree.querySelectorAll(".designer-node").forEach(function (node) {
            var card = node.querySelector(":scope > .designer-card");

            if (!card || node.offsetParent === null) {
                return;
            }

            var depth = 0;

            for (var parent = node.parentElement; parent; parent = parent.parentElement) {
                if (parent.classList && parent.classList.contains("designer-node")) {
                    depth++;
                }
            }

            // Released before measuring, or each pass would measure the height the previous pass
            // imposed and the rows could only ever grow.
            card.style.removeProperty("--designer-card-row-height");

            (byDepth[depth] = byDepth[depth] || []).push(card);
        });

        Object.keys(byDepth).forEach(function (depth) {
            var cards = byDepth[depth];
            var tallest = 0;

            // offsetHeight, not getBoundingClientRect: the canvas carries a CSS zoom, and a
            // rectangle is reported scaled by it. Feeding a scaled height back in as a style
            // would grow the cards a little more on every zoom step.
            cards.forEach(function (card) {
                tallest = Math.max(tallest, card.offsetHeight);
            });

            cards.forEach(function (card) {
                card.style.setProperty("--designer-card-row-height", tallest + "px");
            });
        });
    }

    // Resolves once the node's children are in the DOM, fetching them on the first call only:
    // expanding a second time just shows what is already there.
    function ensureLoaded(li) {
        if (li.getAttribute("data-loaded") === "true") {
            return Promise.resolve();
        }

        var url = childrenUrl + "?" + query({
            structureId: structureId,
            recordId: li.getAttribute("data-record-id"),
            asAt: asAt
        });

        return fetch(url, { headers: { Accept: "application/json" } })
            .then(function (response) { return response.json(); })
            .then(function (children) {
                var list = li.querySelector(".designer-children");

                children.forEach(function (child) {
                    list.appendChild(buildNode(child));
                });

                li.setAttribute("data-loaded", "true");
            });
    }

    // Set by the pan handler when a drag turned into a click, so that dragging the chart by a card
    // does not also expand the card you happened to grab.
    var suppressNextClick = false;

    // ---- the action menu ---------------------------------------------------------------

    // The menu is a <details>, which is what makes it work with no script at all. What <details>
    // does not do is close when you click somewhere else: left alone, every menu you opened stays
    // open, and a chart ends up wearing three of them at once. Everything below is that one
    // missing behaviour and nothing more — opening is still the element's own business.

    // Set when a click outside an open menu dismissed it. That click belongs to the dismissal and
    // to nothing else: it must not also toggle the card it landed on or begin a drag, which is the
    // difference between "click away to close" and "click away to close and accidentally
    // reorganise the company".
    var dismissedMenu = false;

    function openMenus() {
        return Array.prototype.slice.call(surface.querySelectorAll("details.designer-actions[open]"));
    }

    function closeMenus(except) {
        var closed = false;

        openMenus().forEach(function (menu) {
            if (menu !== except) {
                menu.open = false;
                closed = true;
            }
        });

        return closed;
    }

    // Opening one closes the rest. "toggle" does not bubble, so this listens in the capture phase
    // rather than on each <details> — the tree grows cards as branches load, and a listener per
    // menu would have to be attached to each new one.
    surface.addEventListener("toggle", function (event) {
        var menu = event.target;

        if (menu.classList && menu.classList.contains("designer-actions") && menu.open) {
            closeMenus(menu);
        }
    }, true);

    // Capture, and on the document: a click anywhere dismisses, including on the chart's own
    // canvas, the side panel and the page around them. Capture so this runs before the handlers
    // that would otherwise act on the same click.
    document.addEventListener("pointerdown", function (event) {
        var insideMenu = event.target.closest && event.target.closest("details.designer-actions");

        if (closeMenus(insideMenu)) {
            // Only when the pointer went down outside every menu. A click on another card's
            // summary legitimately closes the first menu and opens the second, and that second
            // opening is not something to suppress.
            if (!insideMenu) {
                dismissedMenu = true;
                suppressNextClick = true;
            }
        }
    }, true);

    // The whole card toggles, not only the little control on it. Reaching for the name of the unit
    // is what people do first, and a card that quietly ignores it reads as a broken screen. The
    // control stays, because it is the part that is reachable from the keyboard and that says, in
    // its own label and count, that there is something to open.
    // On the surface rather than on the tree, so an unplaced card behaves like any other: it is
    // the same partial, so it has the same toggle, the same menu and the same card to click.
    surface.addEventListener("click", function (event) {
        if (suppressNextClick) {
            suppressNextClick = false;
            return;
        }

        // Anything genuinely interactive inside a card keeps its own click — including the action
        // menu, which opens and closes on its own and must not also expand the card it sits on.
        if (event.target.closest("a, input, select, textarea, details, summary")) {
            return;
        }

        var li = event.target.closest(".designer-node");

        if (!li) {
            return;
        }

        var button = event.target.closest(".designer-toggle");

        if (button) {
            if (button.disabled) {
                return;
            }
        } else {
            var card = event.target.closest(".designer-card");

            // closest() finds the nearest card, which for a click on a child's card is the child's.
            // Only the node's own card toggles it, and only when there is something to open.
            if (!card || card.parentElement !== li) {
                return;
            }

            if (Number(li.getAttribute("data-child-count")) === 0) {
                return;
            }
        }

        var expanded = li.getAttribute("data-expanded") === "true";

        ensureLoaded(li).then(function () {
            setExpanded(li, !expanded);
        });
    });

    // ---- view switching ---------------------------------------------------------------

    // The view class lives on the viewport, not on the tree inside it, so that the chart's own
    // scrolling box applies in one view and not the other while every descendant rule still
    // matches the same markup.
    function isChart() {
        return !viewport || viewport.classList.contains("designer-chart");
    }

    function applyView(view) {
        var chart = view === "Chart";

        if (viewport) {
            viewport.classList.toggle("designer-chart", chart);
            viewport.classList.toggle("designer-list", !chart);
        }

        if (zoomControls) {
            zoomControls.hidden = !chart;
        }

        if (!chart) {
            setZoom(1);
        }

        surface.ownerDocument.querySelectorAll("[data-designer-view]").forEach(function (link) {
            var active = link.getAttribute("data-designer-view") === view;
            link.classList.toggle("btn-primary", active);
            link.classList.toggle("btn-outline-secondary", !active);
        });

        if (viewCookie) {
            document.cookie = viewCookie + "=" + view + ";path=/;max-age=31536000;samesite=lax";
        }

        // The list hides nothing and needs no levelling; the chart, arriving from the list, has
        // never been measured.
        levelRows();
    }

    document.querySelectorAll("[data-designer-view]").forEach(function (link) {
        link.addEventListener("click", function (event) {
            // Without script these are ordinary links and the server renders the other view;
            // with it, the switch is a class swap that keeps every branch already expanded.
            event.preventDefault();
            applyView(link.getAttribute("data-designer-view"));
        });
    });

    // ---- zoom and pan, chart only ------------------------------------------------------

    function setZoom(value) {
        if (canvas) {
            canvas.style.setProperty("--designer-zoom", String(value));
        }
    }

    function currentZoom() {
        if (!canvas) {
            return 1;
        }

        return parseFloat(canvas.style.getPropertyValue("--designer-zoom")) || 1;
    }

    function fitToScreen() {
        if (!canvas || !viewport) {
            return;
        }

        // Measured unzoomed, because zoom scales the layout box the measurement comes from.
        setZoom(1);

        var available = viewport.clientWidth - 16;
        var needed = canvas.scrollWidth;

        // A much lower floor than the zoom buttons', and deliberately. Their floor stops someone
        // stepping down into a chart nobody can read; this one is the answer to "show me the whole
        // thing", and a fit that stops at 0.3 and leaves a branch off the screen has answered a
        // different question. A fully expanded fifteen-unit tree in a narrow panel needs about
        // 0.28, so the old floor cut the leftmost branch off at exactly the width the control
        // exists for.
        setZoom(needed > available && needed > 0 ? Math.max(0.1, available / needed) : 1);
    }

    if (zoomControls) {
        zoomControls.hidden = !isChart();

        zoomControls.addEventListener("click", function (event) {
            var button = event.target.closest("[data-designer-zoom]");

            if (!button) {
                return;
            }

            var action = button.getAttribute("data-designer-zoom");

            if (action === "fit") {
                fitToScreen();
            } else if (action === "in") {
                setZoom(Math.min(2, currentZoom() * 1.2));
            } else {
                // Never upward. Fit to screen is allowed below this floor, and a zoom-out button
                // that zooms a fitted chart back in is a button doing the opposite of its label.
                setZoom(Math.min(currentZoom(), Math.max(0.3, currentZoom() / 1.2)));
            }
        });
    }

    // ---- panning and dragging ----------------------------------------------------------
    //
    // One pointer, two gestures, told apart by where it went down. Empty canvas pans the chart;
    // a card begins a move — but only after it has travelled far enough to be a deliberate drag,
    // because every click on a card starts with a pointerdown on a card and a move that could be
    // triggered by a twitch would be the worst mutation on the screen to trigger by accident.
    //
    // Nothing commits here. A completed drag navigates to the move screen with the parent filled
    // in, where the preview and the confirmation are the same ones the keyboard route gets.

    var DragThreshold = 8;

    {
        var panning = null;
        var dragging = null;

        function endDrag() {
            if (dragging && dragging.target) {
                dragging.target.classList.remove("designer-drop-target");
            }

            if (dragging && dragging.started) {
                dragging.node.classList.remove("designer-dragging");
                surface.classList.remove("designer-is-dragging");
                suppressNextClick = true;
            }

            dragging = null;
        }

        // On the surface, not on the chart's viewport: an unplaced card is a card, and the whole
        // point of dragging one is that it starts in the side panel and ends on the tree. Panning
        // is still the viewport's own business, because only the chart scrolls.
        surface.addEventListener("pointerdown", function (event) {
            if (event.target.closest("button, a, input, select, textarea, details, summary")) {
                return;
            }

            // This pointerdown was the one that dismissed an open menu. Clicking away to close a
            // menu is not a gesture that should also pick a card up.
            if (dismissedMenu) {
                dismissedMenu = false;
                return;
            }

            suppressNextClick = false;

            var card = event.target.closest(".designer-card");
            var node = card ? card.closest(".designer-node") : null;

            // Only a real unit, only when this viewer may move one. The structure's own card is
            // not a thing that can be reparented.
            if (node && canMove && !node.classList.contains("designer-node-structure")) {
                dragging = {
                    node: node,
                    x: event.clientX,
                    y: event.clientY,
                    started: false,
                    target: null
                };

                return;
            }

            if (!isChart() || !viewport || !viewport.contains(event.target)) {
                return;
            }

            panning = {
                x: event.clientX,
                y: event.clientY,
                left: viewport.scrollLeft,
                top: viewport.scrollTop,
                moved: false
            };

            viewport.classList.add("is-panning");
        });

        // Tracked on the document, so a drag that leaves the panel it began in keeps going. A
        // handler bound to the viewport loses the pointer the moment it crosses into the sidebar,
        // which is the one journey this whole gesture exists for.
        document.addEventListener("pointermove", function (event) {
            if (dragging) {
                var travelled = Math.abs(event.clientX - dragging.x) + Math.abs(event.clientY - dragging.y);

                if (!dragging.started && travelled < DragThreshold) {
                    return;
                }

                if (!dragging.started) {
                    dragging.started = true;
                    dragging.node.classList.add("designer-dragging");
                    surface.classList.add("designer-is-dragging");
                }

                // elementFromPoint, because the card being dragged is not following the pointer —
                // what is under it is whatever the drop would land on.
                var over = document.elementFromPoint(event.clientX, event.clientY);
                var card = over ? over.closest(".designer-card") : null;
                var node = card ? card.closest(".designer-node") : null;

                // Its own branch is not a destination: a unit cannot be dropped inside itself.
                if (node && (node === dragging.node || dragging.node.contains(node))) {
                    node = null;
                }

                if (dragging.target !== node) {
                    if (dragging.target) {
                        dragging.target.classList.remove("designer-drop-target");
                    }

                    dragging.target = node;

                    if (node) {
                        node.classList.add("designer-drop-target");
                    }
                }

                return;
            }

            if (!panning) {
                return;
            }

            var dx = event.clientX - panning.x;
            var dy = event.clientY - panning.y;

            // A few pixels of travel is a click with an unsteady hand, not a drag.
            if (Math.abs(dx) + Math.abs(dy) > 4) {
                panning.moved = true;
            }

            viewport.scrollLeft = panning.left - dx;
            viewport.scrollTop = panning.top - dy;
        });

        document.addEventListener("pointerup", function () {
            if (dragging && dragging.started && dragging.target && moveUrl) {
                var recordId = dragging.node.getAttribute("data-record-id");
                var parentId = dragging.target.classList.contains("designer-node-structure")
                    ? ""
                    : dragging.target.getAttribute("data-record-id");

                endDrag();

                // To the preview, never straight to the write.
                window.location.href = moveUrl + "?" + query({
                    structureId: structureId,
                    recordId: recordId,
                    parentId: parentId,
                    asAt: asAt
                });

                return;
            }

            endDrag();

            if (panning && panning.moved) {
                suppressNextClick = true;
            }

            panning = null;

            if (viewport) {
                viewport.classList.remove("is-panning");
            }
        });

        document.addEventListener("pointercancel", function () {
            endDrag();

            panning = null;

            if (viewport) {
                viewport.classList.remove("is-panning");
            }
        });

        // Escape abandons a drag in progress, which is the only way out of one that does not
        // involve letting go somewhere and hoping.
        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape" && dragging) {
                endDrag();
            }
        });
    }

    // Escape closes an open menu, and puts the focus back on the control that opened it — a menu
    // dismissed from the keyboard that leaves the focus nowhere is a menu a keyboard user cannot
    // get out of. Registered outside the drag block above, because it is about the menu whether or
    // not this view can be dragged at all.
    document.addEventListener("keydown", function (event) {
        if (event.key !== "Escape") {
            return;
        }

        var open = openMenus();

        if (open.length === 0) {
            return;
        }

        var summary = open[0].querySelector("summary");

        closeMenus(null);

        if (summary) {
            summary.focus();
        }
    });

    // ---- search ------------------------------------------------------------------------

    var searchInput = document.getElementById("designer-search");
    var resultsList = document.getElementById("designer-search-results");
    var searchTimer = null;

    function clearResults() {
        if (resultsList) {
            resultsList.innerHTML = "";
            resultsList.hidden = true;
        }
    }

    function renderResults(hits) {
        resultsList.innerHTML = "";

        if (hits.length === 0) {
            resultsList.hidden = true;
            return;
        }

        hits.forEach(function (hit) {
            var item = document.createElement("li");
            item.className = "list-group-item list-group-item-action";
            // The name in the reader's language, with the other in brackets where there is one.
            // Search matches both halves whatever the UI language — an English name is findable
            // under Arabic and the other way round — so a row that only ever showed the English
            // name left an Arabic reader looking at a hit they could not see the match in.
            var arabic = document.documentElement.getAttribute("dir") === "rtl";
            var primary = (arabic ? hit.nameAr : hit.nameEn) || hit.nameEn || hit.nameAr || "";
            var alternate = (arabic ? hit.nameEn : hit.nameAr) || "";

            item.textContent = primary
                + (alternate && alternate !== primary ? " (" + alternate + ")" : "")
                + " (" + hit.dimensionTypeNameEn + " — " + hit.code + ")";
            item.addEventListener("click", function () {
                expandPath(hit);
            });
            resultsList.appendChild(item);
        });

        resultsList.hidden = false;
    }

    // Walks a chain of ids top-down, so every id after the first is already in the DOM by the
    // time it is looked up. Works the same in both views: they are the same nodes. Shared by
    // search and by the return from an action, which both have to reveal one unit deep in a tree.
    function revealPath(ids, done) {
        var index = 0;

        function next() {
            if (index >= ids.length) {
                var target = findNode(ids[ids.length - 1]);

                if (target) {
                    target.scrollIntoView({ block: "center", inline: "center" });
                    target.classList.add("designer-node-highlight");
                    setTimeout(function () {
                        target.classList.remove("designer-node-highlight");
                    }, 2000);
                }

                if (done) {
                    done();
                }

                return;
            }

            var li = findNode(ids[index]);

            if (!li) {
                if (done) {
                    done();
                }

                return;
            }

            ensureLoaded(li).then(function () {
                setExpanded(li, true);
                index += 1;
                next();
            });
        }

        next();
    }

    function expandPath(hit) {
        revealPath(hit.ancestorRecordIds.concat([hit.recordId]), function () {
            clearResults();
            searchInput.value = "";
        });
    }

    // Opening on a particular unit, because an action has just returned here and its result
    // should be on screen rather than inside a branch the user has to find and reopen.
    var expandPathOnLoad = (surface.getAttribute("data-expand-path") || "")
        .split(",")
        .filter(function (id) { return id.length > 0; });

    if (expandPathOnLoad.length > 0) {
        revealPath(expandPathOnLoad);
    }

    // The roots the server drew are a row like any other, and the fonts they are drawn in may not
    // have arrived when the script runs.
    levelRows();

    if (document.fonts && document.fonts.ready) {
        document.fonts.ready.then(levelRows);
    }

    if (searchInput && resultsList && searchUrl) {
        searchInput.addEventListener("input", function () {
            var text = searchInput.value.trim();

            if (searchTimer) {
                clearTimeout(searchTimer);
            }

            if (text.length === 0) {
                clearResults();
                return;
            }

            searchTimer = setTimeout(function () {
                var url = searchUrl + "?" + query({ structureId: structureId, q: text, asAt: asAt });

                fetch(url, { headers: { Accept: "application/json" } })
                    .then(function (response) { return response.json(); })
                    .then(renderResults);
            }, 300);
        });
    }

    // ---- the toolbar -------------------------------------------------------------------

    document.querySelectorAll("[data-designer-autosubmit]").forEach(function (control) {
        control.addEventListener("change", function () {
            control.form.submit();
        });
    });
})();
