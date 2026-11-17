// Class-level isolation lets integration tests share one Postgres container per class
// without racing ResetAsync across methods in the same fixture.
[assembly: Parallelize(Scope = ExecutionScope.ClassLevel)]
