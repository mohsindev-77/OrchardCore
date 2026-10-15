// The grade picker's dependence on the position beside it. Decision D26 G: the grade control lists
// only the chosen position's allowed grades, it is empty and disabled until a position is chosen,
// and it is refreshed when the position changes.
//
// The other half of this file is mode one — a Position's own allowed grades, which is a list of
// checkboxes the server renders complete. It needs no script to be used: every box is in the page,
// named and valued, and ticking and saving is a plain form post. What the script adds is the search
// box (rendered hidden, because filtering is over markup that is already here) and a live count.
//
// Without this script the control still works: the server renders it with whatever position the
// form already held, so a reader who opens an employee with a position sees that position's grades
// and can choose one. What they do not get is the list changing as they change the position — and
// the write is refused in that case by IDimensionValidator.ValidateEmployeeGradeAsync, which is the
// authority either way. A control is not a rule.
(function () {
    "use strict";

    function enhance(wrapper) {
        var select = wrapper.querySelector("select[data-grade-picker]");
        var positionFieldId = wrapper.getAttribute("data-grade-picker-position-field") || "";

        if (!select || !positionFieldId) {
            return;
        }

        var position = document.getElementById(positionFieldId);

        if (!position) {
            return;
        }

        var url = wrapper.getAttribute("data-grade-picker-url");
        var asAt = wrapper.getAttribute("data-grade-picker-as-at") || "";
        var multiple = wrapper.getAttribute("data-grade-picker-multiple") === "true";
        var strings = parseJson(wrapper.getAttribute("data-grade-picker-strings"));
        var waiting = wrapper.querySelector("[data-grade-picker-waiting]");
        var empty = wrapper.querySelector("[data-grade-picker-empty]");

        // The hidden inputs the server renders so a disabled control does not erase a stored grade
        // on save. Once this script is driving the control the control itself posts, so they would
        // be a second value for one field.
        function dropHeldValues() {
            Array.prototype.forEach.call(
                wrapper.querySelectorAll("[data-grade-picker-held]"),
                function (held) { held.remove(); });
        }

        function say(message) {
            if (waiting) {
                waiting.hidden = !message;
                waiting.textContent = message || "";
            }

            if (empty) {
                empty.hidden = true;
            }
        }

        function render(items, keep) {
            var chosen = keep || [];

            select.innerHTML = "";

            if (!multiple) {
                select.appendChild(new Option(strings.choose || "", ""));
            }

            items.forEach(function (item) {
                var option = new Option(item.name + " (" + item.code + ")", item.id);

                option.setAttribute("data-code", item.code);
                option.selected = chosen.indexOf(item.id) >= 0;

                select.appendChild(option);
            });

            select.disabled = items.length === 0;

            say(items.length === 0
                ? (position.value ? strings.none : strings.waiting)
                : "");
        }

        function refresh() {
            dropHeldValues();

            if (!position.value) {
                render([], []);

                return;
            }

            // What was chosen before, so a reader who changes the position and changes it back does
            // not silently lose their grade. A grade the new position does not allow simply does
            // not come back, which is D26 G working rather than a value being dropped.
            var chosen = Array.prototype.filter
                .call(select.options, function (option) { return option.selected && option.value; })
                .map(function (option) { return option.value; });

            var parameters = new URLSearchParams();

            parameters.set("positionId", position.value);

            if (asAt) {
                parameters.set("asAt", asAt);
            }

            fetchJson(url + "?" + parameters.toString(), function (found) {
                render(found.items || [], chosen);
            });
        }

        position.addEventListener("change", refresh);

        var date = document.getElementById(wrapper.getAttribute("data-grade-picker-date-field") || "");

        if (date) {
            date.addEventListener("change", function () {
                asAt = date.value || "";
                refresh();
            });
        }
    }

    // Mode one: the allowed-grades checklist. Search and a live count, over boxes already in the
    // page.
    function enhanceChecklist(wrapper) {
        var list = wrapper.querySelector("[data-grade-checklist]");
        var search = wrapper.querySelector("[data-grade-checklist-search]");
        var count = wrapper.querySelector("[data-grade-checklist-count]");
        var noMatch = wrapper.querySelector("[data-grade-checklist-no-match]");

        if (!list) {
            return;
        }

        var strings = parseJson(wrapper.getAttribute("data-grade-checklist-strings"));
        var options = Array.prototype.slice.call(list.querySelectorAll("[data-grade-option]"));

        function boxOf(option) {
            return option.querySelector("input[type=checkbox]");
        }

        function tell() {
            if (!count) {
                return;
            }

            var chosen = options.filter(function (option) {
                var box = boxOf(option);

                return box && box.checked;
            }).length;

            // One number into one template. The words come from the server, which is where the
            // localiser is; see GradePickerTagHelper.Chosen for why the phrase has no plural in it.
            count.textContent = (strings.selected || "{0}").replace("{0}", String(chosen));
        }

        function filter() {
            var query = (search && search.value ? search.value : "").trim().toLowerCase();
            var shown = 0;

            options.forEach(function (option) {
                var box = boxOf(option);

                // A ticked grade is never hidden. Hiding it would take a chosen value out of the
                // reader's sight while leaving it in the post, so the form would save something the
                // screen said was not there.
                var keep = (box && box.checked) ||
                    query === "" ||
                    (option.getAttribute("data-label") || "").toLowerCase().indexOf(query) >= 0 ||
                    (option.getAttribute("data-code") || "").toLowerCase().indexOf(query) >= 0;

                option.hidden = !keep;

                if (keep) {
                    shown += 1;
                }
            });

            if (noMatch) {
                noMatch.hidden = shown > 0;
                noMatch.textContent = shown > 0 ? "" : (strings.noMatch || "");
            }
        }

        if (search) {
            search.hidden = false;
            search.addEventListener("input", filter);
        }

        list.addEventListener("change", tell);

        tell();
    }

    function parseJson(value) {
        try {
            return JSON.parse(value || "{}");
        } catch (error) {
            return {};
        }
    }

    function fetchJson(url, then) {
        fetch(url, { credentials: "same-origin", headers: { Accept: "application/json" } })
            .then(function (response) { return response.ok ? response.json() : null; })
            .then(function (found) { if (found) { then(found); } })
            .catch(function () { /* leave the control as the server rendered it */ });
    }

    function start() {
        Array.prototype.forEach.call(
            document.querySelectorAll("[data-grade-picker-url]"), enhance);

        Array.prototype.forEach.call(
            document.querySelectorAll("[data-grade-checklist-strings]"), enhanceChecklist);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", start);
    } else {
        start();
    }
})();
