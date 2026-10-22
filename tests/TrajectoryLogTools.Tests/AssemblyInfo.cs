// Program.Main and the CLI tests redirect Console.Out / Console.Error, which is process-wide.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
