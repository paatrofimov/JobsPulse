namespace JobsPulse.Tests.Integration.SuccessFactors;

/// <summary>
/// The fixtures are cut out of live career sites rather than written by hand: the whole point of these tests is that
/// the documents SuccessFactors actually serves are read correctly, and a hand-written sample only proves the parser
/// agrees with whoever wrote it.
/// </summary>
public static class SuccessFactorsFixtures
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Integration", "SuccessFactors", "Fixtures", name));

    public static Stream Open(string name) =>
        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Read(name)));
}
