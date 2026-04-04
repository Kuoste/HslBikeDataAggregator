---
applyTo: "**/*"
---
# Repository Workflow Guidelines

- Track implementation work on an open GitHub issue.
- Use an issue branch named `issue-<number>-<short-description>` for changes.
- If an issue was closed before its code was pushed, reopen the issue before continuing delivery.
- Add or update automated tests for every delivered behavior or repository-level configuration change.
- Run `dotnet build HslBikeDataAggregator.slnx` and the relevant automated tests before considering the issue complete.
- Do not treat an issue as done until the code is committed on the issue branch and ready to merge.
