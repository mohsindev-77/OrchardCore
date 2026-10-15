// Progressive enhancement for the unit picker: a search box over the whole structure, with each
// option showing its full path, in place of a <select> that could only hold a page of it.
//
// The combobox is workmate-picker.js in WorkMate.Platform — the same control the employee picker
// drives, for the same reasons and with the same guarantees. What is here is this picker's own
// two questions: where it searches, and what an option reads as.
//
// The one thing this control has that the employee picker does not is the "within" filter. It is
// rendered hidden by the server and unhidden here, because without this script it would be a
// control that does nothing: the <select> already holds the units it would narrow, and there is no
// round trip behind it. A dead control is worse than an absent one.
(function () {
    "use strict";

    function enhance(wrapper) {
        var picker = window.WorkMatePicker;
        var select = wrapper.querySelector("select[data-unit-picker]");

        if (!picker || !select) {
            return;
        }

        var strings = picker.parseJson(wrapper.getAttribute("data-unit-picker-strings"));
        var url = wrapper.getAttribute("data-unit-picker-url");
        var structureId = wrapper.getAttribute("data-unit-picker-structure") || "";
        var asAt = wrapper.getAttribute("data-unit-picker-as-at") || "";

        // Set when the control is choosing a parent for a new record of this type rather than a
        // place to put an employee. It changes which question the server answers, and with it the
        // permission the endpoint checks.
        var childType = wrapper.getAttribute("data-unit-picker-child-type") || "";

        var fallbackNote = wrapper.querySelector("[data-unit-picker-fallback-note]");

        if (fallbackNote) {
            fallbackNote.remove();
        }

        var withinRow = wrapper.querySelector("[data-unit-picker-within-row]");
        var within = wrapper.querySelector("[data-unit-picker-within]");

        if (withinRow) {
            withinRow.hidden = false;
        }

        var control = picker.enhance({
            wrapper: wrapper,
            select: select,
            strings: strings,
            // Normally never: not choosing a unit is how a row is left blank, and the form says so;
            // an explicit "— nowhere —" would be a placement at nowhere, which is not a thing.
            //
            // The exception is choosing a parent for a type this structure allows at the top of the
            // chart. That record genuinely has no parent, which is a choice somebody makes rather
            // than a box they left empty, so it is an option with its own words.
            allowNone: wrapper.getAttribute("data-unit-picker-allow-none") === "true",
            search: function (query, deliver, fail) {
                var parameters = new URLSearchParams();

                parameters.set("structureId", structureId);
                parameters.set("q", query || "");
                parameters.set("take", "20");

                if (asAt) {
                    parameters.set("asAt", asAt);
                }

                if (childType) {
                    parameters.set("childType", childType);
                }

                if (within && within.value) {
                    parameters.set("within", within.value);
                }

                picker.fetchJson(
                    url + "?" + parameters.toString(),
                    function (found) {
                        deliver({
                            total: found.total,
                            items: (found.items || []).map(function (item) {
                                return {
                                    id: item.id,
                                    // The path, then the code. Two units called "Credit" are told
                                    // apart by what is above them, so the path leads; the code is
                                    // what somebody searching by code recognises.
                                    label: item.path + " (" + item.code + ")"
                                };
                            })
                        });
                    },
                    fail);
            }
        });

        // Narrowing the scope re-runs the search rather than filtering what is on screen: the
        // results are a page of the whole structure, so a client-side filter could only ever narrow
        // the twenty that happened to arrive.
        //
        // It also has to resolve the contradiction it can create. Somebody picks Civil, then
        // narrows Within to Corporate: the form now holds a unit the filter beside it says is out
        // of bounds, and leaving both standing means saving a choice the screen has disowned. The
        // choice is dropped and the drop is SAID — a control that silently emptied itself would
        // send the reader to Save believing a unit was still named.
        //
        // The server answers whether the choice is in scope, because this cannot: the chosen unit
        // may be nowhere near the page of twenty that arrived.
        var cleared = wrapper.querySelector("[data-unit-picker-cleared]");

        function sayCleared(message) {
            if (cleared) {
                cleared.hidden = !message;
                cleared.textContent = message || "";
            }
        }

        if (within && control) {
            within.addEventListener("change", function () {
                sayCleared("");

                var chosen = select.value;

                if (!chosen || !within.value) {
                    control.refresh();

                    return;
                }

                var parameters = new URLSearchParams();

                parameters.set("structureId", structureId);
                parameters.set("q", "");
                parameters.set("take", "1");
                parameters.set("within", within.value);
                parameters.set("check", chosen);

                if (asAt) {
                    parameters.set("asAt", asAt);
                }

                if (childType) {
                    parameters.set("childType", childType);
                }

                picker.fetchJson(
                    url + "?" + parameters.toString(),
                    function (answer) {
                        if (answer && answer.checkInScope === false) {
                            control.clear();
                            sayCleared(strings.cleared || "");
                        }

                        control.refresh();
                    },
                    // The server could not be asked. The choice is left exactly as it was rather
                    // than cleared on a guess: dropping a unit because a request failed would be
                    // the control losing somebody's work over a dropped packet.
                    function () { control.refresh(); });
            });
        }

        // Typing a new unit after the scope cleared one means the note has stopped being true.
        select.addEventListener("change", function () { sayCleared(""); });

        // The effective date decides which units are active, so a form that changes it changes what
        // may be chosen. Re-asked rather than left stale, because a unit that closed before the new
        // date must stop being offered the moment the date says so.
        if (asAt && control) {
            var date = document.getElementById(wrapper.getAttribute("data-unit-picker-date-field") || "");

            if (date) {
                date.addEventListener("change", function () {
                    wrapper.setAttribute("data-unit-picker-as-at", date.value || "");
                    asAt = date.value || "";
                    control.refresh();
                });
            }
        }
    }

    if (window.WorkMatePicker) {
        window.WorkMatePicker.whenReady("[data-unit-picker-url]", enhance);
    }
})();
