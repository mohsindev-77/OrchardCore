// Progressive enhancement for the position picker: a search box over every position in the tenant,
// each option showing its unit and how full it is, in place of a <select> that could only hold a
// page of them. Decision D26 F.
//
// The combobox is workmate-picker.js in WorkMate.Platform — the same control the unit and employee
// pickers drive. What is here is this picker's own three things: where it searches, what an option
// reads as, and the warning that a chosen position is already at its approved headcount.
//
// That warning is the only part of D26 F this script owns outright. "A full position warns and is
// allowed" cannot be said by a disabled option or a missing one, because both of those would be
// refusing it; it has to be a sentence that appears beside the control when the choice is made.
(function () {
    "use strict";

    function enhance(wrapper) {
        var picker = window.WorkMatePicker;
        var select = wrapper.querySelector("select[data-position-picker]");

        if (!picker || !select) {
            return;
        }

        var strings = picker.parseJson(wrapper.getAttribute("data-position-picker-strings"));
        var url = wrapper.getAttribute("data-position-picker-url");
        var asAt = wrapper.getAttribute("data-position-picker-as-at") || "";
        var excluding = wrapper.getAttribute("data-position-picker-excluding") || "";

        // The axis the choice will be written on, when the tenant has more than one Position type.
        // Without it the search would offer a position the save is going to refuse.
        var structureId = wrapper.getAttribute("data-position-picker-structure") || "";

        // The department this post sits in, whose own posts are offered first — slice F6c.
        var near = wrapper.getAttribute("data-position-picker-near") || "";
        var full = wrapper.querySelector("[data-position-picker-full]");

        var fallbackNote = wrapper.querySelector("[data-position-picker-fallback-note]");

        if (fallbackNote) {
            fallbackNote.remove();
        }

        // Which positions are full, learned from the options the server rendered and kept up to
        // date as searches bring more back. Held here rather than re-asked on every change: the
        // answer arrives with the option and asking again would be a round trip to repeat it.
        var fullById = {};

        Array.prototype.forEach.call(select.options, function (option) {
            if (option.value) {
                fullById[option.value] = option.getAttribute("data-full") === "true";
            }
        });

        function showWarning() {
            if (full) {
                full.hidden = !(select.value && fullById[select.value]);
            }
        }

        var control = picker.enhance({
            wrapper: wrapper,
            select: select,
            strings: strings,
            // "Reports to nobody" is a real answer — the CEO has one — so it is an option with its
            // own words rather than an empty box somebody did not finish.
            allowNone: wrapper.getAttribute("data-position-picker-allow-none") === "true",
            search: function (query, deliver, fail) {
                var parameters = new URLSearchParams();

                parameters.set("q", query || "");
                parameters.set("take", "20");

                if (asAt) {
                    parameters.set("asAt", asAt);
                }

                if (excluding) {
                    parameters.set("excluding", excluding);
                }

                if (structureId) {
                    parameters.set("structureId", structureId);
                }

                if (near) {
                    parameters.set("near", near);
                }

                picker.fetchJson(
                    url + "?" + parameters.toString(),
                    function (found) {
                        deliver({
                            total: found.total,
                            items: (found.items || []).map(function (item) {
                                fullById[item.id] = item.full;

                                // The name, then the code, then the unit, then the count. Two posts
                                // called "Supervisor" are told apart by the unit; the count is what
                                // shows somebody they are about to over-fill one.
                                return {
                                    id: item.id,
                                    label: item.name + " (" + item.code + ")"
                                        + (item.unit ? " — " + item.unit : "")
                                        + " · " + item.filled + "/" + item.approved
                                };
                            })
                        });
                    },
                    fail);
            }
        });

        select.addEventListener("change", showWarning);
        showWarning();

        // The effective date decides which positions are active, so a form that changes it changes
        // what may be chosen. Re-asked rather than left stale.
        if (control) {
            var date = document.getElementById(wrapper.getAttribute("data-position-picker-date-field") || "");

            if (date) {
                date.addEventListener("change", function () {
                    wrapper.setAttribute("data-position-picker-as-at", date.value || "");
                    asAt = date.value || "";
                    control.refresh();
                });
            }
        }
    }

    if (window.WorkMatePicker) {
        window.WorkMatePicker.whenReady("[data-position-picker-url]", enhance);
    }
})();
