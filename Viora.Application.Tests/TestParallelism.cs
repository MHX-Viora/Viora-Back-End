using Xunit;

// Each PG test owns a cluster. Bound class concurrency; in-test races stay concurrent.
[assembly: CollectionBehavior(MaxParallelThreads = 2)]
