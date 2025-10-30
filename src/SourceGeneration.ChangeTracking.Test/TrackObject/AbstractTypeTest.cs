namespace SourceGeneration.ChangeTracking;

[TestClass]
public class AbstractTypeTest
{
    [TestMethod]
    public void Test()
    {
        DerivedType tracking = new();

        ((ICascadingChangeTracking)tracking).AcceptChanges();

        Assert.IsFalse(((ICascadingChangeTracking)tracking).IsChanged);
        Assert.IsFalse(((ICascadingChangeTracking)tracking).IsCascadingChanged);

        tracking.A = 1;
        Assert.IsTrue(((ICascadingChangeTracking)tracking).IsChanged);
        Assert.IsFalse(((ICascadingChangeTracking)tracking).IsCascadingChanged);

    }
}

[ChangeTracking]
public abstract partial class AbstractType
{
    public partial int A { get; set; }
}

[ChangeTracking]
public partial class DerivedType :AbstractType
{
    public partial int B { get; set; }
}


[ChangeTracking]
public sealed partial class SealedDerivedType : AbstractType
{
    public partial int B { get; set; }
}

public class AbstractType2
{
    public int A { get; set; }
}

[ChangeTracking]
public sealed partial class SealedDerivedType2 : AbstractType2
{
    public partial int B { get; set; }
}