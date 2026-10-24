# edit-seed

The post-deploy seed edited: rows added to or changed in a MERGE the post-deploy script runs after every publish.

```text
recognize:
  match: altered PostDeploymentScript Text
  match: created PostDeploymentScript
  ships: one release, with the post-deploy seed
```
