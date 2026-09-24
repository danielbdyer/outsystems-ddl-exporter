# handoffs/ — the transient home for cross-session persona handoffs

When intake, change-author, and the reviewer run as **separate sessions**, the artifacts they
pass — the change-spec, the review packet — need a place to live between sessions. This is
it: one directory per change, `handoffs/<change-id>/`, holding the change-spec and the
review-packet files as the personas produce them.

Lifetime: **transient — swept when the change's pull request merges.** The pull request body
is the durable record (`skills/author-pr`); a handoff file that outlives its merged PR is
residue, removed on the next deprecation-train sweep. Within one continuous session the
handoff needs no file at all — this directory exists for the real multi-session case, never
as a second copy of the PR.
