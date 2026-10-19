# FastEdit Agent Instructions

## Build and Test Validation

- Use GitHub Actions as the default build and test path. Do not compile or run
  tests locally by default; local validation requires a user request.
- Verify the Actions results for the exact pushed commit. Report failed, blocked,
  or pending checks explicitly rather than claiming validation is complete.
- For changes affecting desktop behavior, run the full UI/E2E suite, not only the
  push-triggered smoke tests:
  `gh workflow run ci.yml --ref master -f full_ui=true`.

## Git Workflow

- Pull requests are not required. When committing and pushing are authorized,
  use the direct-to-`master` workflow; do not create a PR unless requested.
- Do not commit, push, tag, or publish without user authorization.

## Desktop Test Safety

- Keep `tests\FastEdit.UiTests` separate from `FastEdit.sln`, so ordinary
  `dotnet test` does not drive a desktop.
- Run desktop tests only in an isolated, unlocked Windows account without an
  existing FastEdit profile or process. Preserve the harness's opt-in, profile
  ownership, and foreground-input guards.
- Do not dismiss OS consent/security prompts or bypass test safety guards to
  make a run pass.

## Packaging and Releases

- Package the CI-built binaries only after required checks pass; do not rebuild
  between validation and packaging.
- Do not change `build.counter` during routine validation. Version changes, tags,
  and publication belong to an explicitly requested release.
- Use the configured production signing certificate. Do not generate or
  substitute a production key, or upload a developer's private key, without
  explicit authorization. Disposable CI test certificates are not release keys.

See [Automated Builds and Tests](README.md#automated-builds-and-tests) and
[Signed Releases](README.md#signed-releases) for pipeline triggers, current runner
coverage, diagnostics, and certificate configuration. Keep transient test counts,
run URLs, and current release versions out of this file.
