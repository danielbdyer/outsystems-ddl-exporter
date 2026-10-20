# identity-swap

IDENTITY added to or removed from an existing column. IDENTITY is fixed when a column is created, so DacFx rebuilds the
table: a new table, every row copied with IDENTITY_INSERT on, and every incoming foreign key dropped and created again
around the copy.

```text
recognize:
  match: altered Column IsIdentity:false→true IdentitySeed:any IdentityIncrement:any
  match: altered Column IsIdentity:true→false IdentitySeed:any IdentityIncrement:any
  ships: one release as a rebuild of the table without incoming foreign keys; several releases with them
```
