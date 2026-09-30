# WorkMate 2.0

A multi-tenant SaaS platform on Orchard Core. Human capital management is the
first domain; the foundation is domain-neutral so later domains are additions.

## Start here
1. `CLAUDE.md` — standing instructions for every Claude Code session. Read it first.
2. `docs/README.md` — export the four specification documents into `docs/` (required before prompt 1).
3. `docs/adr/` — decisions already taken. ADR-0001 needs the Orchard Core version confirmed.
4. `docs/prompt-library.md` — the sequenced briefs. Prompt 0 is done by this bootstrap; begin at prompt 1.

## Build
    dotnet restore
    dotnet build -warnaserror
    dotnet test

The solution builds as an empty shell: every module has a manifest, startup,
permissions stub, README and test project, and no business code. That is
deliberate — the first content type is written in prompt 2, against the
specification, after the Orchard Core version is confirmed.

## Layout
See `CLAUDE.md` for the repository layout and conventions.
