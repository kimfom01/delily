namespace Delily.ConformanceTests;

public sealed class CorpusTests
{
    [Fact]
    public void CorpusDirectory_Exists()
    {
        var root = FindRepositoryRoot();
        var corpus = Path.Combine(root, "tests", "corpus");

        Assert.True(Directory.Exists(corpus), $"Missing conformance corpus at {corpus}.");
        Assert.True(File.Exists(Path.Combine(corpus, "README.md")), "Missing corpus README.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "delily.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate delily.slnx above the test output directory.");
    }
}
