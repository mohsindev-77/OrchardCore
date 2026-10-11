// Progressive enhancement for the employee picker: a search box over the whole tenant, in place
// of a <select> that could only ever hold a page of it.
//
// The <select> stays. It is still the form control — still named, still posted, still bound, still
// validated — and this script hides it and sets its value. So a form works identically whether or
// not this file loaded, which matters because these forms commit dated decisions; the only thing
// lost without it is the ability to reach somebody who is not on the first page.
//
// The pattern is the WAI-ARIA combobox: a text input with role="combobox", a listbox of options,
// and the keys people expect — Down/Up to move, Enter to choose, Escape to close, Home/End to jump.
// aria-activedescendant moves the screen reader's focus without moving the browser's, which is what
// keeps typing and choosing in the same control.
(function () {
    "use strict";

    var DEBOUNCE_MS = 200;

    function parseStrings(wrapper) {
        try {
            return JSON.parse(wrapper.getAttribute("data-employee-picker-strings") || "{}");
        } catch (error) {
            return {};
        }
    }

    function format(template, values) {
        return String(template || "").replace(/\{(\d+)\}/g, function (match, index) {
            var value = values[Number(index)];

            return value === undefined ? match : value;
        });
    }

    function enhance(wrapper) {
        var select = wrapper.querySelector("select[data-employee-picker]");

        if (!select || select.dataset.enhanced === "true") {
            return;
        }

        select.dataset.enhanced = "true";

        var strings = parseStrings(wrapper);
        var url = wrapper.getAttribute("data-employee-picker-url");
        var includeExited = wrapper.getAttribute("data-employee-picker-include-exited") === "true";
        var allowNone = wrapper.getAttribute("data-employee-picker-allow-none") === "true";
        var fixedQuery = wrapper.getAttribute("data-employee-picker-query") || "";

        // The caption the server rendered advises narrowing a list that is about to stop existing.
        var fallbackNote = wrapper.querySelector("[data-employee-picker-fallback-note]");

        if (fallbackNote) {
            fallbackNote.remove();
        }

        var id = select.id;
        var listId = id + "__list";
        var statusId = id + "__status";

        // Hidden from sight and from the accessibility tree, because it is now plumbing: leaving it
        // exposed would give a screen reader two controls for one question.
        select.classList.add("visually-hidden");
        select.setAttribute("aria-hidden", "true");
        select.setAttribute("tabindex", "-1");

        var box = document.createElement("div");

        box.className = "workmate-picker-box position-relative";

        var input = document.createElement("input");

        input.type = "text";
        input.className = "form-control workmate-picker-input";
        input.setAttribute("role", "combobox");
        input.setAttribute("autocomplete", "off");
        input.setAttribute("aria-expanded", "false");
        input.setAttribute("aria-controls", listId);
        input.setAttribute("aria-autocomplete", "list");
        input.setAttribute("aria-describedby", statusId + " " + id + "__error");
        input.placeholder = strings.search || "";

        // The label the server rendered points at the <select>. Repointed, so clicking it focuses
        // the control somebody can actually type in.
        var label = wrapper.querySelector('label[for="' + id + '"]');

        if (label) {
            input.id = id + "__input";
            label.setAttribute("for", input.id);
        }

        var list = document.createElement("ul");

        list.className = "list-group workmate-picker-list d-none";
        list.id = listId;
        list.setAttribute("role", "listbox");

        var status = document.createElement("div");

        // aria-live, so the count and "no match" are announced rather than only drawn.
        status.className = "form-text";
        status.id = statusId;
        status.setAttribute("aria-live", "polite");

        // The field error, which is a different thing from the status line and must not live in it.
        // The status is a running commentary on the search — "Showing 20 of about 158", "No match" —
        // and it is overwritten every time a result lands, including by a request that was already
        // in flight when the reader pressed the button. An error written there is gone a moment
        // later, which is how a refusal comes to look like nothing happening.
        var error = document.createElement("div");

        error.className = "invalid-feedback";
        error.id = id + "__error";

        box.appendChild(input);
        box.appendChild(list);
        box.appendChild(error);
        select.insertAdjacentElement("afterend", box);
        box.insertAdjacentElement("afterend", status);

        var options = [];
        var active = -1;
        var timer = null;
        var sequence = 0;

        // Who is chosen, as a fact of its own rather than something inferred from the text in the
        // box. Inferring it was the defect: the box holds the chosen person's full label, the label
        // is not a search term, and every code path that re-read the box as though it were — the
        // focus handler, most visibly — searched for "Adeel Bhatti (zenith-labour-b-12) — Site Team
        // B", found nobody, and announced "No match" at somebody who had just chosen successfully.
        //
        // The posted value survived all that, which made it worse rather than better: the control
        // said the selection had failed while quietly holding a correct one. The opposite case is
        // the dangerous one and had the same cause — editing the text after choosing left the old
        // id in the <select>, so a form could post somebody the reader had visibly moved away from.
        var chosen = null;

        function selectedOption() {
            return select.options[select.selectedIndex] || null;
        }

        // What was in the <select> when the page arrived: a line manager already on the record, a
        // head already in post. Counts as a selection, or the first thing the reader did would be
        // told it had chosen nobody.
        (function () {
            var initial = selectedOption();

            if (initial && initial.value) {
                chosen = { id: initial.value, label: initial.textContent };
            }
        })();

        /// The text to search for, which is empty when the box is merely showing the selection.
        function queryText() {
            var text = input.value.trim();

            return chosen && text === chosen.label ? "" : text;
        }

        function showChosen() {
            input.value = chosen ? chosen.label : "";
        }

        /// Marks the control as holding text that names nobody, or clears that state.
        function markUnresolved(unresolved) {
            input.classList.toggle("is-invalid", unresolved);
            input.setAttribute("aria-invalid", unresolved ? "true" : "false");
            error.textContent = unresolved ? (strings.unresolved || "") : "";
        }

        function clearChosen() {
            chosen = null;
            select.value = "";
            select.dispatchEvent(new Event("change", { bubbles: true }));
        }

        function close() {
            list.classList.add("d-none");
            list.innerHTML = "";
            input.setAttribute("aria-expanded", "false");
            input.removeAttribute("aria-activedescendant");
            options = [];
            active = -1;
        }

        function choose(option) {
            if (!option) {
                return;
            }

            // The chosen person may not be among the <option>s the server rendered, which is the
            // entire point of searching. Added when missing, so the posted value is always one the
            // control holds.
            var existing = Array.prototype.find.call(select.options, function (candidate) {
                return candidate.value === option.id;
            });

            if (!existing) {
                existing = document.createElement("option");
                existing.value = option.id;
                select.appendChild(existing);
            }

            existing.textContent = option.label;
            select.value = option.id;
            select.dispatchEvent(new Event("change", { bubbles: true }));

            // "Nobody", where a form offers it, is a choice and not a selection: there is no person
            // to name afterwards, and treating it as one would put the word "— nobody —" in the box
            // and then search for it.
            chosen = option.id ? { id: option.id, label: option.label } : null;

            input.value = chosen ? chosen.label : "";
            status.textContent = chosen ? format(strings.chosen, [chosen.label]) : "";
            markUnresolved(false);
            close();
        }

        function highlight(next) {
            if (options.length === 0) {
                return;
            }

            if (active >= 0 && list.children[active]) {
                list.children[active].classList.remove("active");
            }

            active = (next + options.length) % options.length;

            var element = list.children[active];

            element.classList.add("active");
            input.setAttribute("aria-activedescendant", element.id);
            element.scrollIntoView({ block: "nearest" });
        }

        function render(found, query) {
            list.innerHTML = "";
            options = [];
            active = -1;

            // "Nobody" is an option on the forms that have one, and it belongs in the list rather
            // than only in the hidden <select>: clearing a choice has to be reachable the same way
            // making one is.
            if (allowNone && !query) {
                options.push({ id: "", label: strings.none || "", code: "", where: "" });
            }

            found.items.forEach(function (item) {
                options.push({
                    id: item.id,
                    label: item.where
                        ? item.name + " (" + item.code + ") — " + item.where
                        : item.name + " (" + item.code + ")",
                    code: item.code,
                    where: item.where || ""
                });
            });

            if (options.length === 0) {
                status.textContent = strings.empty || "";
                close();

                return;
            }

            options.forEach(function (option, index) {
                var entry = document.createElement("li");

                entry.id = listId + "__" + index;
                entry.className = "list-group-item list-group-item-action";
                entry.setAttribute("role", "option");
                entry.setAttribute("aria-selected", "false");
                entry.textContent = option.label;

                // mousedown, not click: click fires after blur, and blur closes the list, so a
                // click handler would be attached to an element already gone.
                entry.addEventListener("mousedown", function (event) {
                    event.preventDefault();
                    choose(option);
                });

                list.appendChild(entry);
            });

            list.classList.remove("d-none");
            input.setAttribute("aria-expanded", "true");

            status.textContent = found.total > found.items.length
                ? format(strings.more, [found.items.length, found.total])
                : format(strings.all, [found.items.length]);
        }

        function search(query) {
            var mine = ++sequence;
            var parameters = new URLSearchParams();

            // The form's own narrowing, when it has one, is applied on top of what was typed rather
            // than replaced by it.
            parameters.set("q", [fixedQuery, query].filter(Boolean).join(" ").trim());
            parameters.set("includeExited", includeExited ? "true" : "false");
            parameters.set("take", "20");

            fetch(url + "?" + parameters.toString(), { headers: { Accept: "application/json" } })
                .then(function (response) { return response.json(); })
                .then(function (found) {
                    // A slower earlier request must not overwrite a faster later one, or the list
                    // ends up showing the results for a prefix of what is in the box.
                    if (mine === sequence) {
                        render(found, query);
                    }
                })
                .catch(function () {
                    if (mine === sequence) {
                        status.textContent = strings.empty || "";
                        close();
                    }
                });
        }

        function schedule() {
            window.clearTimeout(timer);
            timer = window.setTimeout(function () { search(queryText()); }, DEBOUNCE_MS);
        }

        // Editing the text abandons the selection, before anything is searched for. The box and the
        // posted value must never disagree: a reader who types over "Adeel Bhatti" has stopped
        // choosing Adeel Bhatti, and a control that kept his id while showing somebody else's name
        // would post a person nobody on the screen had named.
        input.addEventListener("input", function () {
            if (chosen && input.value.trim() !== chosen.label) {
                clearChosen();
            }

            markUnresolved(false);
            schedule();
        });

        input.addEventListener("focus", function () {
            if (options.length === 0) {
                // queryText(), not the box's contents. The box holds a chosen person's full label —
                // name, code and unit — which is not a search term and matches nobody.
                search(queryText());
            }
        });

        input.addEventListener("keydown", function (event) {
            switch (event.key) {
                case "ArrowDown":
                    event.preventDefault();

                    if (options.length === 0) {
                        search(input.value.trim());
                    } else {
                        highlight(active + 1);
                    }

                    break;
                case "ArrowUp":
                    event.preventDefault();
                    highlight(active - 1);
                    break;
                case "Home":
                    if (options.length > 0) {
                        event.preventDefault();
                        highlight(0);
                    }

                    break;
                case "End":
                    if (options.length > 0) {
                        event.preventDefault();
                        highlight(options.length - 1);
                    }

                    break;
                case "Enter":
                    if (active >= 0) {
                        // Only when something is highlighted: Enter on an untouched list is the
                        // reader submitting the form, which is theirs to do.
                        event.preventDefault();
                        choose(options[active]);
                    }

                    break;
                case "Escape":
                    close();
                    showChosen();
                    break;
                default:
                    break;
            }
        });

        input.addEventListener("blur", function () {
            window.setTimeout(function () {
                close();

                if (chosen) {
                    // Back to naming the selection, in case the reader typed and thought better of
                    // it.
                    showChosen();
                    markUnresolved(false);

                    return;
                }

                // Text that names nobody is left where it is and marked, rather than silently
                // wiped. Wiping it loses what the reader typed and leaves an empty box with no
                // explanation; marking it is the only version in which the message on submit makes
                // sense.
                markUnresolved(input.value.trim().length > 0);
            }, 120);
        });

        // The last line of defence, and the one that turns a silent wrong answer into a visible
        // refusal. A box with text in it and nothing chosen means the reader typed a name and never
        // picked anybody from the list — so there is no id to post, and submitting would either
        // save nothing or save whoever was chosen before.
        if (input.form) {
            input.form.addEventListener("submit", function (event) {
                if (chosen || input.value.trim().length === 0) {
                    return;
                }

                event.preventDefault();

                // The message goes on the field, not in the status line: a search that was already
                // in flight will land a moment later and overwrite the status, which would leave
                // the reader looking at a form that refused to submit and said nothing about why.
                markUnresolved(true);
                window.clearTimeout(timer);
                input.focus();
            });
        }

        showChosen();
        markUnresolved(false);
    }

    function enhanceAll(root) {
        (root || document).querySelectorAll("[data-employee-picker-url]").forEach(enhance);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", function () { enhanceAll(document); });
    } else {
        enhanceAll(document);
    }
})();
