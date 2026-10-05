# WorkMate.Browser.Tests

The only suite that runs the product's own JavaScript.

Every other test here talks to the server: it asserts on rendered HTML, or calls
an endpoint and checks the JSON. That cannot see anything that happens after the
page arrives. The organisation designer's expand control shipped doing nothing
at all, and the whole suite stayed green, because the markup was right, the
`Children` endpoint was right, and no test ever clicked anything.

This suite closes that gap for behaviour that is genuinely browser-side:
expanding and collapsing a branch, switching view without a reload, zoom and
pan, search that opens the branch holding a match, and right-to-left mirroring
under Arabic. It is not a replacement for the server-level tests — permissions,
effective dating and what the endpoints return belong in
`WorkMate.Integration.Tests`, where they are far cheaper to assert.

## Before it can run

Playwright needs real browser binaries, which are not restored by NuGet:

```
dotnet build tests/WorkMate.Browser.Tests
tests/WorkMate.Browser.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Without them every test fails at launch. A build agent needs the same step.

## How the tenant is built

`BrowserTenantFixture` mirrors `BaseTenantFixture` — its own content root under
the temp directory, the recipes and PO files copied in, `Production` so Orchard
uses the views compiled into the module assemblies — with one difference: a
browser needs a socket, so the fixture starts a real Kestrel host on a dynamic
port alongside the in-memory host `WebApplicationFactory` requires, and
everything in the suite talks to the Kestrel address.

The tenant is then set up over HTTP with `base.recipe.json` and seeded by running
the shipped demo recipe from `/Admin/Recipes`, the same way an operator would. So
what the browser sees is what someone following the module README would see, and
the sample organisation the assertions name — Sales, Retail, In-Store Sales — is
the demo recipe's, not a fixture's private fiction.

Signing in happens once and every test gets a fresh context built from the saved
cookies: a clean page each time, with no remembered view carried between tests,
without paying for the login form nine times over.

## The real-data suite

`RealDataTenantFixture` runs the host against a **copy** of a developer's own
`App_Data` — their tenant, their organisation, their settings — in whichever real
browser the machine has (Edge or Chrome by channel, falling back to bundled
Chromium). It exists because a fresh tenant built from recipes is not the same
thing as a tenant with history: older settings, structures created by hand as
well as by recipe, schema that arrived through upgrades. A defect reported from a
real browser on a real tenant gets a test against a real tenant.

It never touches the original. The copy is made before the host starts, the
content root points at the copy, and the fixture compares the real database's
size and timestamp before and after, failing loudly if they ever differ.

By default it copies `src/WorkMate.Web/App_Data`. Set `WORKMATE_REAL_APPDATA` to
copy from somewhere else — a snapshot taken before a tenant changed, or a copy of
a customer's data sent in with a bug report:

```
$env:WORKMATE_REAL_APPDATA = "C:\path\to\App_Data"
dotnet test tests/WorkMate.Browser.Tests
```

The designer tests there need the organisation designer demo recipe to have been
run on that tenant. When it has not, the fixture reports why and those tests stand
down rather than failing against a tree that was never there — so a green run of
this suite on a clean clone, or on a tenant without the demo data, is not evidence
about the designer. Check the skip reason before reading anything into it.

## Writing a test here

Drive the screen, do not reach into it. Click the control a person would click
and assert on what becomes visible.

Every test that exercises the script collects console errors, page errors and
failed requests and asserts there were none. A script that throws while loading
leaves the page looking completely correct and every control inert — which is
the failure this project exists for, and it is worth catching directly rather
than only through the symptom.
