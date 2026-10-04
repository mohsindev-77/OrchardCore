// Progressive enhancement for the attribute schema builder on the dimension type editor. Plain
// JavaScript, no framework: the form works (add rows server-side on redisplay, remove rows by
// leaving the field name blank so the server drops it) even if this script never loads; this only
// makes doing so pleasant.
(function () {
    "use strict";

    var table = document.getElementById("attribute-schema-table");
    var addButton = document.getElementById("add-attribute-row");
    var template = document.getElementById("attribute-row-template");

    if (!table || !addButton || !template) {
        return;
    }

    var body = table.querySelector("tbody");

    function nextIndex() {
        return body.querySelectorAll("tr.attribute-row").length;
    }

    function addRow() {
        var index = nextIndex();
        var html = template.innerHTML.split("__index__").join(String(index));
        var holder = document.createElement("tbody");
        holder.innerHTML = html;

        var row = holder.querySelector("tr.attribute-row");
        body.appendChild(row);
    }

    function removeRow(row) {
        row.remove();
        reindex();
    }

    // Field names must stay contiguous and zero-based for ASP.NET Core's default model binder to
    // read the whole list back; removing a row from the middle would otherwise leave a gap.
    function reindex() {
        var rows = body.querySelectorAll("tr.attribute-row");

        rows.forEach(function (row, index) {
            row.querySelectorAll("[name]").forEach(function (field) {
                field.name = field.name.replace(/AttributeSchema\[\d+\]/, "AttributeSchema[" + index + "]");
            });
        });
    }

    addButton.addEventListener("click", addRow);

    body.addEventListener("click", function (event) {
        var button = event.target.closest(".remove-attribute-row");

        if (button) {
            removeRow(button.closest("tr.attribute-row"));
        }
    });
})();
