/*
    Tooltips on the Organisation menu's entries — decision D24.

    A dimension's values are in the admin sidebar now, and a value's caption can carry a parent and
    an organisation after it: "Credit – Lahore Main Branch (Crescent)". The sidebar is narrow and
    the theme truncates with an ellipsis, so the end of a long caption is unreadable exactly when it
    is the part that disambiguates.

    This puts the full caption in the link's `title`, which is what a browser shows on hover and
    what a screen reader announces. Nothing is computed: the text is already complete in the DOM —
    the truncation is CSS — so this copies it rather than rebuilding it, and a caption the server
    changes needs no change here.

    WHY A SCRIPT RATHER THAN THE MARKUP. Orchard Core 3.0.1's MenuItem has no title or attribute
    bag — verified against the pinned assemblies with tools/api-probe: it carries Text, Href, Id,
    Classes, Permissions and RouteValues, and nothing else — and the admin theme's templates are
    compiled into TheAdmin.dll, where a module cannot override them and a theme would win if it
    could. Adding one would mean changing Orchard, which CLAUDE.md forbids outright. A script that
    decorates the rendered menu is an extension of it.

    It is an enhancement, and the menu is complete without it: every caption is fully present in the
    page, every link works, and a reader with no JavaScript loses a hover hint rather than a
    destination.
*/
(function () {
    'use strict';

    // The links this module put in the menu, found by where they go rather than by a class: the
    // theme decides where it renders a MenuItem's classes, and the href is ours either way.
    var selector =
        "a[href*='/Dimensions/Records/Details'], a[href*='/Dimensions/Records/Index']";

    function title(link) {
        // Never overwrite a title something else set, and never add an empty one.
        if (link.getAttribute('title')) {
            return;
        }

        var caption = (link.textContent || '').trim();

        if (caption) {
            link.setAttribute('title', caption);
        }
    }

    function titleAllWithin(root) {
        if (root.matches && root.matches(selector)) {
            title(root);
        }

        if (root.querySelectorAll) {
            Array.prototype.slice.call(root.querySelectorAll(selector)).forEach(title);
        }
    }

    titleAllWithin(document);

    // AND AGAIN WHENEVER THE MENU GROWS, which is not a refinement — it is most of the job.
    //
    // A nested group's contents are not in the page at the moment it loads: measured on a real
    // tenant, the control for a dimension group does not exist in the DOM until "Organisation"
    // above it has been expanded, and its values do not exist until the dimension itself has been.
    // A one-pass script therefore titles the handful of links that happen to be rendered already
    // and none of the ones a reader actually hovers.
    //
    // An observer rather than a hover handler: the attribute has to be on the element before the
    // browser decides to show a tooltip, and "set it when the pointer arrives" is a race with the
    // browser's own delay that is lost on fast hardware and won on slow, which is the worst kind.
    if (typeof MutationObserver !== 'function') {
        return;
    }

    new MutationObserver(function (mutations) {
        mutations.forEach(function (mutation) {
            Array.prototype.slice.call(mutation.addedNodes).forEach(function (node) {
                if (node.nodeType === 1) {
                    titleAllWithin(node);
                }
            });
        });
    }).observe(document.body, { childList: true, subtree: true });
})();
