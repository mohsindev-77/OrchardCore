// Progressive enhancement for the structure editor's level builder. Plain JavaScript, no
// framework: order is expressed by field index, so even with this script absent the form still
// posts the rows in the order they were rendered in.
(function () {
    "use strict";

    var table = document.getElementById("levels-table");
    var addButton = document.getElementById("add-level-row");
    var template = document.getElementById("level-row-template");

    if (!table || !addButton || !template) {
        return;
    }

    var body = table.querySelector("tbody");

    function rows() {
        return Array.prototype.slice.call(body.querySelectorAll("tr.level-row"));
    }

    function addRow() {
        var index = rows().length;
        var html = template.innerHTML.split("__index__").join(String(index));
        var holder = document.createElement("tbody");
        holder.innerHTML = html;

        var row = holder.querySelector("tr.level-row");
        body.appendChild(row);
        reindex();
    }

    function removeRow(row) {
        row.remove();
        reindex();
    }

    function moveUp(row) {
        var previous = row.previousElementSibling;
        if (previous) {
            body.insertBefore(row, previous);
            reindex();
        }
    }

    function moveDown(row) {
        var next = row.nextElementSibling;
        if (next) {
            body.insertBefore(next, row);
            reindex();
        }
    }

    // Field names must stay zero-based and contiguous, in document order, for the posted order
    // to be the order IStructureService assigns ordinals from. The displayed ordinal is one-based
    // (level 1 is the root) purely for the person reading it; the posted index underneath is not.
    function reindex() {
        rows().forEach(function (row, index) {
            row.querySelector(".level-ordinal").textContent = String(index + 1);

            row.querySelectorAll("[name]").forEach(function (field) {
                field.name = field.name.replace(/LevelDimensionTypeIds\[\d+\]/, "LevelDimensionTypeIds[" + index + "]");
            });
        });
    }

    // ---- the containment grid ----------------------------------------------------------

    // The grid's rows and columns are the types chosen above, so editing that list has to redraw
    // it. The server renders the first copy — the screen works with no script at all, which is
    // what keeps it usable and testable without one — and everything below only keeps it in step
    // with a list the user is changing in front of it.

    var shape = document.getElementById("structure-shape");
    var grid = document.getElementById("containment-grid");
    var rootTypes = document.getElementById("root-types");
    var chainButton = document.getElementById("build-from-chain");

    // Which ticks are set, kept here rather than read off the DOM, so that a type removed and put
    // back does not silently lose the rules that were written about it.
    var ticked = {};
    var roots = {};

    function remember() {
        if (!grid || !rootTypes) {
            return;
        }

        grid.querySelectorAll("input[name='ContainmentPairs']").forEach(function (box) {
            ticked[box.value] = box.checked;
        });

        rootTypes.querySelectorAll("input[name='RootDimensionTypeIds']").forEach(function (box) {
            roots[box.value] = box.checked;
        });
    }

    // The chosen types, in the order the rows are in, with their labels taken from the option that
    // is selected: the select already carries every label and the self-nesting flag, so the grid
    // needs no second copy of the type list to go stale against.
    function vocabulary() {
        var chosen = [];
        var seen = {};

        rows().forEach(function (row) {
            var select = row.querySelector("select");
            var value = select ? select.value : "";

            if (!value || seen[value]) {
                return;
            }

            seen[value] = true;

            var option = select.options[select.selectedIndex];

            chosen.push({
                id: value,
                label: option ? option.textContent.trim() : value,
                selfNesting: option ? option.getAttribute("data-self-nesting") === "true" : false
            });
        });

        return chosen;
    }

    function checkbox(name, value, checked, disabled, label) {
        var box = document.createElement("input");

        box.className = "form-check-input";
        box.type = "checkbox";
        box.name = name;
        box.value = value;
        box.checked = checked;
        box.disabled = disabled;

        if (label) {
            box.setAttribute("aria-label", label);
        }

        return box;
    }

    function redraw() {
        if (!grid || !rootTypes) {
            return;
        }

        remember();

        var types = vocabulary();

        rootTypes.textContent = "";

        types.forEach(function (type) {
            var wrapper = document.createElement("div");
            wrapper.className = "form-check";

            var box = checkbox("RootDimensionTypeIds", type.id, roots[type.id] === true, false, type.label);
            box.id = "root-" + type.id;

            var label = document.createElement("label");
            label.className = "form-check-label";
            label.htmlFor = box.id;
            label.textContent = type.label;

            wrapper.appendChild(box);
            wrapper.appendChild(label);
            rootTypes.appendChild(wrapper);
        });

        var head = grid.querySelector("thead tr");
        var bodyRows = grid.querySelector("tbody");

        while (head.children.length > 1) {
            head.removeChild(head.lastElementChild);
        }

        types.forEach(function (type) {
            var cell = document.createElement("th");
            cell.scope = "col";
            cell.className = "text-center";
            cell.textContent = type.label;
            head.appendChild(cell);
        });

        bodyRows.textContent = "";

        types.forEach(function (parent) {
            var row = document.createElement("tr");
            row.setAttribute("data-parent", parent.id);

            var heading = document.createElement("th");
            heading.scope = "row";
            heading.textContent = parent.label;
            row.appendChild(heading);

            types.forEach(function (child) {
                var cell = document.createElement("td");
                cell.className = "text-center";
                cell.setAttribute("data-child", child.id);

                // The diagonal where the type itself forbids nesting: disabled rather than
                // unticked, because no structure can grant past the type's own veto and an offer
                // the validator would refuse is worse than no offer.
                var vetoed = parent.id === child.id && !parent.selfNesting;
                var value = parent.id + ">" + child.id;

                cell.appendChild(checkbox(
                    "ContainmentPairs", value, !vetoed && ticked[value] === true, vetoed, parent.label + " > " + child.label));

                row.appendChild(cell);
            });

            bodyRows.appendChild(row);
        });

        grid.hidden = types.length === 0;
    }

    // Every type may contain the next one, and the first sits at the top: the plain chain that was
    // the only shape expressible before ADR-0010, offered as a starting point rather than as the
    // rule. Replaces what is there rather than adding to it, because "build" is what the button
    // says and a half-merged grid is nobody's intention.
    function buildFromChain() {
        var types = vocabulary();

        ticked = {};
        roots = {};

        types.forEach(function (type, index) {
            if (index === 0) {
                roots[type.id] = true;
            }

            if (index + 1 < types.length) {
                ticked[type.id + ">" + types[index + 1].id] = true;
            }
        });

        redrawWithoutRemembering();
    }

    function redrawWithoutRemembering() {
        var remembered = remember;
        remember = function () { };
        redraw();
        remember = remembered;
    }

    addButton.addEventListener("click", function () {
        addRow();
        redraw();
    });

    body.addEventListener("click", function (event) {
        var row = event.target.closest("tr.level-row");

        if (!row) {
            return;
        }

        if (event.target.closest(".remove-level-row")) {
            removeRow(row);
        } else if (event.target.closest(".move-level-up")) {
            moveUp(row);
        } else if (event.target.closest(".move-level-down")) {
            moveDown(row);
        } else {
            return;
        }

        redraw();
    });

    body.addEventListener("change", function (event) {
        if (event.target.matches("select")) {
            redraw();
        }
    });

    if (chainButton) {
        chainButton.addEventListener("click", buildFromChain);
    }

    if (shape) {
        remember();
    }
})();
