// Progressive enhancement for the employee picker: a search box over the whole tenant, in place
// of a <select> that could only ever hold a page of it.
//
// The combobox itself — the keyboard handling, the race guard, the submit refusal, the rule that
// the box and the posted value never disagree — is workmate-picker.js, which this and the unit
// picker both drive. What is left here is the two things that are the employee picker's own: where
// it searches, and what an option reads as.
(function () {
    "use strict";

    function enhance(wrapper) {
        var picker = window.WorkMatePicker;
        var select = wrapper.querySelector("select[data-employee-picker]");

        if (!picker || !select) {
            return;
        }

        var strings = picker.parseJson(wrapper.getAttribute("data-employee-picker-strings"));
        var url = wrapper.getAttribute("data-employee-picker-url");
        var includeExited = wrapper.getAttribute("data-employee-picker-include-exited") === "true";
        var fixedQuery = wrapper.getAttribute("data-employee-picker-query") || "";

        // The caption the server rendered advises narrowing a list that is about to stop existing.
        var fallbackNote = wrapper.querySelector("[data-employee-picker-fallback-note]");

        if (fallbackNote) {
            fallbackNote.remove();
        }

        picker.enhance({
            wrapper: wrapper,
            select: select,
            strings: strings,
            allowNone: wrapper.getAttribute("data-employee-picker-allow-none") === "true",
            search: function (query, deliver, fail) {
                var parameters = new URLSearchParams();

                // The form's own narrowing, when it has one, is applied on top of what was typed
                // rather than replaced by it.
                parameters.set("q", [fixedQuery, query].filter(Boolean).join(" ").trim());
                parameters.set("includeExited", includeExited ? "true" : "false");
                parameters.set("take", "20");

                picker.fetchJson(
                    url + "?" + parameters.toString(),
                    function (found) {
                        deliver({
                            total: found.total,
                            items: (found.items || []).map(function (item) {
                                return {
                                    id: item.id,
                                    // Where they sit after the code, and omitted when they are not
                                    // placed: an unplaced new starter is ordinary, and a dash would
                                    // read as a lookup that failed. The same sentence the server
                                    // builds for the <option> text, so a chosen person reads
                                    // identically whether the script ran or not.
                                    label: item.where
                                        ? item.name + " (" + item.code + ") — " + item.where
                                        : item.name + " (" + item.code + ")"
                                };
                            })
                        });
                    },
                    fail);
            }
        });
    }

    // The core is a declared dependency of this resource, so it is on the page before this runs.
    // Guarded anyway: a page that somehow referenced this one by path alone would otherwise throw
    // here and take every other script on the page down with it.
    if (window.WorkMatePicker) {
        window.WorkMatePicker.whenReady("[data-employee-picker-url]", enhance);
    }
})();
