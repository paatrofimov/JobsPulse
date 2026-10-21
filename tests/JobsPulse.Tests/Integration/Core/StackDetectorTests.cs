using FluentAssertions;
using JobsPulse.Core.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Core;

/// <summary>
/// «.NET is one of the main languages» against «.NET is a plus», and «another language is required» against «it is
/// one of the alternatives» - the two readings the split .NET watchlists are built on.
/// </summary>
public sealed class StackDetectorTests
{
    private static readonly string[] Terms = [" .net", "(.net", "/.net", " c#", "/c#", "dotnet"];

    private static bool IsStack(string s) => Terms.Any(t => s.Contains(t, StringComparison.OrdinalIgnoreCase));

    [Test]
    public void Read_should_take_the_title_as_primary()
    {
        StackDetector.Read(".NET Developer", "We build things.", IsStack).Primary.Should().BeTrue();
    }

    [TestCase("<ul><li>5+ years of experience with C# and .NET</li></ul>")]
    [TestCase("<ul><li>Proficiency in C# .NET, Java, or Go, with a strong understanding of design</li></ul>")]
    [TestCase("<p>Strong hands-on programming skills, ideally in C#/.NET</p>")]
    public void Read_should_take_a_requirement_as_primary(string description)
    {
        StackDetector.Read("Senior Software Engineer", description, IsStack).Primary.Should().BeTrue();
    }

    [TestCase("<ul><li>Strong Java experience</li><li>C# and .NET experience a plus</li></ul>")]
    [TestCase("<ul><li>Exposure to C#, .NET and REST APIs is a bonus</li></ul>")]
    [TestCase("<p>Requirements</p><ul><li>Go</li></ul><p>Nice to have</p><ul><li>.NET</li></ul>")]
    public void Read_should_not_take_a_nice_to_have_as_primary(string description)
    {
        StackDetector.Read("Senior Software Engineer", description, IsStack).Primary.Should().BeFalse();
    }

    [TestCase("<ul><li>Support the build, deployment, and operation of modern .NET applications</li></ul>")]
    [TestCase("<p>Tech stack: Azure Cloud, .NET 8, ASP.NET Core services, Python, Mongo</p>")]
    [TestCase("<p>Backend: Microservices written in C# ASP.NET Core 8 & Golang</p>")]
    [TestCase("<p>Support build and deployment for backend (.NET) and frontend (JavaScript/TypeScript) applications</p>")]
    public void Read_should_take_the_environment_as_core(string description)
    {
        StackDetector.Read("Senior DevOps Engineer", description, IsStack).Core.Should().BeTrue();
    }

    [TestCase("<ul><li>Proficiency in C# .NET, Java, or Go, with a strong understanding of design</li></ul>")]
    [TestCase("<ul><li>Proficient in one or more of: C#, Python, Go, Node.js</li></ul>")]
    [TestCase("<ul><li>Experience programming in languages such as Java, C++, Python, Go, Rust, C#</li></ul>")]
    [TestCase("<ul><li>Read and troubleshoot code (Python, C#, Typescript, etc.)</li></ul>")]
    [TestCase("<ul><li>Proficiency in at least two of: Python, C++, C#, Rust.</li></ul>")]
    public void Read_should_not_take_one_of_the_alternatives_as_core(string description)
    {
        var reading = StackDetector.Read("Platform Engineer", description, IsStack);

        reading.Primary.Should().BeTrue();
        reading.Core.Should().BeFalse();
    }

    [Test]
    public void Read_should_take_the_title_as_core()
    {
        StackDetector.Read("Senior .NET Platform Engineer", "Python, Java or Go", IsStack).Core.Should().BeTrue();
    }

    [TestCase("<ul><li>5+ years of production experience with Python</li></ul>")]
    [TestCase("<ul><li>Strong proficiency in Go and Kubernetes</li></ul>")]
    public void Read_should_see_another_required_language(string description)
    {
        StackDetector.Read("Site Reliability Engineer", description, IsStack).OtherRequired.Should().BeTrue();
    }

    [TestCase("<ul><li>Scripting experience in Python or Bash</li></ul>")]
    [TestCase("<ul><li>Experience with Terraform and Kubernetes</li><li>Python is a plus</li></ul>")]
    [TestCase("<ul><li>Experience with Java, Python or .NET</li></ul>")]
    [TestCase("<p>Let's go beyond. Experience with observability tooling.</p>")]
    public void Read_should_not_see_another_required_language(string description)
    {
        StackDetector.Read("Site Reliability Engineer", description, IsStack).OtherRequired.Should().BeFalse();
    }
}
