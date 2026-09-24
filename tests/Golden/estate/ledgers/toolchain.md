# The toolchain

One dated row per estate version: the DacFx release the Octopus step's engine runs, pinned, and the
release immediately before it; or UNPINNED, until release engineering names the pin. estate accepts
its committed engine only at the pin or the release before it, and while the row reads UNPINNED
every receipt says so. A new row is appended; an old row is never edited.

| Date | estate | Pinned DacFx | Release before |
|---|---|---|---|
| 2026-09-24 | 3.0.0 | UNPINNED | — |
