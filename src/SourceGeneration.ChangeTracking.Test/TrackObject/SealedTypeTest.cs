using System.Security.Cryptography.X509Certificates;

namespace SourceGeneration.ChangeTracking;

[TestClass]
public class SealedTypeTest
{
    [TestMethod]
    public void Test()
    {
        SealedType tracking = new();

        ((ICascadingChangeTracking)tracking).AcceptChanges();

        Assert.IsFalse(((ICascadingChangeTracking)tracking).IsChanged);
        Assert.IsFalse(((ICascadingChangeTracking)tracking).IsCascadingChanged);

        tracking.A = 1;
        Assert.IsTrue(((ICascadingChangeTracking)tracking).IsChanged);
        Assert.IsFalse(((ICascadingChangeTracking)tracking).IsCascadingChanged);

    }
}

[ChangeTracking]
public sealed partial class SealedType
{
    public partial int A { get; set; }
}