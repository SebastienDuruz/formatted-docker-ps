# Follow-ups outside the structural refactoring

These observations concern behavior already present before extraction. They are
not fixed in this change, which preserves the CLI and UI behavior.

- `DockerProcessRunner` reads stdout and then stderr synchronously. A process
  filling stderr while keeping stdout open could block. A separate change can
  drain both streams concurrently and test it with a controlled child process.
- Docker processes have no timeout or cancellation. Closing the screen prevents
  late UI updates, but does not explicitly terminate an in-flight Docker process.
  Timeout and shutdown policy should be specified before changing that behavior.
- Table fitting uses .NET string length, not terminal display-cell width. Wide
  characters and combining sequences can misalign cells. Supporting them needs
  dedicated Unicode rendering fixtures and a defined width policy.
