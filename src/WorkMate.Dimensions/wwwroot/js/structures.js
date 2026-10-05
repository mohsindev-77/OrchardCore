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

    addButton.addEventListener("click", addRow);

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
        }
    });
})();
