using MarkLeaf.Documents;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class MathNumberPolicyTests
{
    [TestMethod]
    public void OnlyAsciiHierarchyOrEmptyIsAccepted()
    {
        foreach (var value in new[] { "", "1", "1.1", " 12.3 " }) Assert.IsTrue(MathNumberPolicy.IsValid(value));
        foreach (var value in new[] { "１", "1.", "a", "1\n2", "1..2" }) Assert.IsFalse(MathNumberPolicy.IsValid(value));
    }
}
