//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace SourceGeneration.ChangeTracking;

//[TestClass]
//public class ParentStateTest
//{
//    [TestMethod]
//    public void Test()
//    {
//        StateChild child = new();
//        StateParent parent = new();
//        child.Parent = parent;
//        parent.Child = child;


//        bool changed1 = false;
//        bool changed2 = false;

//        using var tracker1 = child.CreateTracker();
//        tracker1.Watch(x => x.C);
//        tracker1.OnChange(() => changed1 = true);

//        using var tracker2 = parent.CreateTracker();
//        tracker2.Watch(x => x.Child.C);
//        tracker2.OnChange(() => changed2 = true);

//        child.C = 2;
//        child.AcceptChanges();


//        Assert.IsTrue(changed1);
//        Assert.IsTrue(changed2);
//    }
//}

//[ChangeTracking]
//public partial class StateChild : State<StateChild>
//{
//    public partial int C { get; set; }

//}

//[ChangeTracking]
//public partial class StateParent : State<StateParent>
//{
//    //public partial int P { get; set; }

//    public partial StateChild Child { get; set; }
//}
