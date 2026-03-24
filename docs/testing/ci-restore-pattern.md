# CI Restore Pattern for Per-Project Test Loops

## The Correct Pattern

When running `dotnet test` against individual projects in a loop (rather than
at solution level), always restore at the solution level first, then use
`--no-restore` in each per-project test command:

```yaml
- name: Restore
  run: dotnet restore ${{ env.SOLUTION_PATH }}

- name: Verify restore completed
  run: |
    if [ ! -d "archonai/tests/ArchonAI.ActionSafety.Tests/obj" ]; then
      echo "::error::Restore artifacts missing"
      exit 1
    fi

- name: Run tests
  run: |
    for proj in "${PROJECTS[@]}"; do
      dotnet test "$proj" --no-restore -c Release ...
    done
```

**Why this works:** `dotnet restore` at solution level generates
`obj/project.assets.json` for every project in the solution graph. The
subsequent `--no-restore` flag tells the SDK to skip the restore phase and
use the already-generated assets file, which is both faster and avoids
redundant network calls to NuGet feeds.

## The Incorrect Pattern (NETSDK1004)

Omitting the solution-level restore and relying on each `dotnet test` call
to restore on its own (or worse, using `--no-restore` without any prior
restore) causes the following error on every project:

```
error NETSDK1004: Assets file '/path/to/obj/project.assets.json' not found.
Run a NuGet package restore to generate this file.
```

This happens because:

1. `--no-restore` explicitly skips NuGet restore, so no `project.assets.json`
   is generated.
2. Without `--no-restore`, individual project restore may still fail if the
   project depends on other projects in the solution whose assets have not
   been resolved yet (dependency ordering issues).
3. The result is that **all tests fail** -- not with test failures, but with
   build errors, making the entire CI stage red with no useful test output.

### Example of the broken pattern

```yaml
# BAD: No restore step before the loop
- name: Run tests
  run: |
    for proj in "${PROJECTS[@]}"; do
      dotnet test "$proj" --no-restore -c Release ...
      # ^ Every project fails with NETSDK1004
    done
```

```yaml
# ALSO BAD: Restoring per-project can miss cross-project dependencies
- name: Run tests
  run: |
    for proj in "${PROJECTS[@]}"; do
      dotnet restore "$proj"
      dotnet test "$proj" --no-restore -c Release ...
    done
```

## Why Per-Project Targeting Instead of Solution-Level Testing

Running `dotnet test ArchonAI.slnx --filter "Category!=Integration"` at the
solution level works functionally, but produces noisy output: every test
project that has zero tests matching the filter emits a warning:

```
No test matches the given testcase filter
```

With 10+ test projects, many of which contain only unit tests (no `Category`
trait), this warning appears repeatedly and obscures real failures. Per-project
targeting eliminates this noise by only applying category filters to projects
that actually use category traits.

## How to Verify the Pattern Is Safe Before Merging CI Changes

Before merging any change to the CI workflow that touches restore or test
steps, verify the following:

1. **Restore step exists** -- Confirm that `dotnet restore $SOLUTION_PATH`
   appears before any `dotnet test` calls in the same job.

2. **Safety check step exists** -- The `Verify restore completed` step in
   `rc-validate.yml` checks for the presence of `obj/` directories after
   restore. If this step fails, the restore did not complete successfully.

3. **`--no-restore` flag is present** -- Every `dotnet test` call in the loop
   should include `--no-restore`. Without it, each test command will attempt
   its own restore, which is slower and may produce inconsistent results.

4. **Local reproduction** -- Run the following locally to simulate the CI
   pattern:
   ```bash
   dotnet restore archonai/ArchonAI.slnx
   ls archonai/tests/ArchonAI.ActionSafety.Tests/obj/project.assets.json
   # Should exist after solution restore

   dotnet test archonai/tests/ArchonAI.ActionSafety.Tests/ArchonAI.ActionSafety.Tests.csproj \
     --no-restore -c Release
   # Should succeed without NETSDK1004
   ```

## Reference

- **Safety check location:** `.github/workflows/rc-validate.yml`, Stage 2
  (unit-tests job), `Verify restore completed` step.
- **Bug origin:** The `ci-cd.yml` workflow previously omitted the
  solution-level restore before per-project test loops, causing NETSDK1004
  failures across all test stages. This was identified and fixed, and the
  `rc-validate.yml` workflow includes an explicit safety check to prevent
  regression.
- **Production readiness:** See `PRODUCTION_READINESS.md` item 3 under
  "Changes in This Branch" for the summary.
