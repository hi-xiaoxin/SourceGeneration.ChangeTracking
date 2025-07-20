# ChangeTracking

[![NuGet](https://img.shields.io/nuget/vpre/SourceGeneration.ChangeTracking.svg)](https://www.nuget.org/packages/SourceGeneration.ChangeTracking)

SourceGeneration.ChangeTracking is a state management framework based on Source Generator, it supports `AOT` compilation.

## Installing

This library uses C# preview features `partial property`, Before using this library, please ensure the following prerequisites are met:
- Visual Studio is version 17.11 preview 3 or higher.
- To enable C# language preview in your project, add the following to your .csproj file
```c#
<PropertyGroup>  
  <LangVersion>preview</LangVersion>  
</PropertyGroup>  
```

```powershell
Install-Package SourceGeneration.ChangeTracking -Version 1.0.0
```

```powershell
dotnet add package SourceGeneration.ChangeTracking --version 1.0.0
```

## Start

States source generator will generate partial class for your state type, you just need to add `ChangeTrackingAttriute`, The state type must be `partial`, The property must be `partial` and have a setter

```c#
[ChangeTracking]
public partial class Goods
{
    public partial int Number { get; set; }
    public partial double Price { get; set; } = 1.0;
    public partial int Count { get; set; }
}
```
The partial class implement `INotifyPropertyChanging`, `INotifyPropertyChanged` and `IChangeTracking`

```c#
public partial class Goods : INotifyPropertyChanging, INotifyPropertyChanged, System.ComponentModel.IChangeTracking
{
    //Properties partial implementation
}
```

States determines whether an object has been modified through two methods:
- Checking if the object reference has changed.
- Checking IChangeTracking.IsChanged property.

## Tracking Changes

There are two ways for state tracking:
- ​Via ChangeTracker
- Using State

The ChangeTracker can track object changes.

```c#
[ChangeTracking]
public partial class Goods
{
    public partial int Number { get; set; }
    public partial double Price { get; set; } = 1.0;
    public partial int Count { get; set; }
}

Goods state = new();
var tracker = new ChangeTracker<Goods>(state);
tracker.Watch(x => x.Price, x => Console.WriteLine($"Price has changed: {x}"));
tracker.Watch(x => x.Count, x => Console.WriteLine($"Count has changed: {x}"));

state.Count++;
tracker.AcceptChanges(); // output: Count has changed: 1

state.Price = 3.14;
tracker.AcceptChanges(); // output: Price has changed: 3.14
```

## State

State internally implements CreateTracker method to simplify tracking operations, allowing it to monitor its own state. Based on ChangeTracking, we can build a state that subscribes to changes.

```c#
[ChangeTracking]
public partial class Goods : State<Goods>
{
    public partial int Number { get; set; }
    public partial double Price { get; set; } = 1.0;
    public partial int Count { get; set; }
}
```
The State class can create a IChangeTracker.
```c#
Goods state = new();
int currentCount = 0;

//Create a IChangeTracker to tracking state changes
var tracker = state.CreateTracker();

//Watch price and count property
tracker.Watch(x => x.Price, x => Console.WriteLine($"Price has changed: {x}"));
tracker.Watch(x => x.Count, x => Console.WriteLine($"Count has changed: {x}"));

state.Count++;
state.AcceptChanges(); // output: Count has changed: 1

state.Price = 3.14;
state.AcceptChanges(); // output: Price has changed: 3.14

state.Number = 1;
state.AcceptChanges(); // no output, because the Number property was not watch

state.Count = 1;
state.AcceptChanges(); // no output, because the Count property has not changed

```

## Predicate

```c#

tracker.Watch(
    selector: x => x.Count,
    predicate: x => x >= 10,
    subscriber: x => Console.WriteLine($"Count changed: {x}"));

// no console ouput, the value is less than 10
state.Count = 9;
state.AcceptChanges();

// ouput Count changed: 10
state.Count = 10;
state.AcceptChanges();

```

## Change Scope
States support change scope, You can specify the scope of the subscribed changes.

- **ChangeTrackingScope.Instance**  
  The subscription only be triggered when there are changes in instance reference of the object itself.
- **ChangeTrackingScope.InstanceProperty** `default value`  
  The subscription only be triggered when there are changes in the properties of the object itself.
- **ChangeTrackingScope.Cascading**  
  The subscription will be triggered when there are changes in the properties of the object itself or in the properties of its property objects.
- **ChangeTrackingScope.Always**  
  The subscription will be triggered whenever the `Update` method is called, regardless of whether the value has changed or not.

```c#
[ChangeTracking]
public partial class Goods : State<Goods>
{
    public partial ChangeTrackingList<SubState> Tags { get; set; } = [];
}

[ChangeTracking]
public partial class SubState
{
    public partial string? Tag { get; set; }
}
```


### ChangeTrackingScope.Instance
```c#
// Watch Tags with scope `ChangeTrackingScope.Root`, it's default value
// The state will push last value when you subscribed
// ouput: Tags count has changed 0
using var disposable = tracker.Watch(
    selector: x => x.Tags, 
    subscriber: x => Console.WriteLine("Tags collection instance has changed"), 
    scope: ChangeTrackingScope.Instance);

// no output, because Tags collection instance is not changed
state.Tags.Add(new SubState { Tag = "first tag" });
state.AcceptChanges();

// output: 'Tags collection instance has changed'
state.Tags = [];
state.AcceptChanges();

```

### ChangeTrackingScope.InstanceProperty
```c#
// Watch Tags with scope `ChangeTrackingScope.Root`, it's default value
// The state will push last value when you subscribed
// ouput: Tags count has changed 0
using var disposable = tracker.Watch(
    selector: x => x.Tags, 
    subscriber: x => Console.WriteLine($"Tags count has changed: {x.Count}"), 
    scope: ChangeTrackingScope.InstanceProperty);

// output: Tags count has changed: 1
state.Tags.Add(new SubState { Tag = "first tag" });
state.AcceptChanges();

// no output, because Tags property is not changed
state.Tags[0].Tag = "first tag has modified";
state.AcceptChanges();

```

### ChangeTrackingScope.Cascading
```c#
// Watch Tags with scope `ChangeTrackingScope.Cascading`
// The state will push last value when you subscribed
// ouput: Tags value has changed: first tag has modified
using var disposable = tracker.Watch(
    selector: x => x.Tags,
    subscriber: x => Console.WriteLine($"Tags value has changed: {x[0].Tag}"),
    scope: ChangeTrackingScope.Cascading);

// ouput: Tags value has changed: first tag has modified * 2
state.Tags[0].Tag = "first tag has modified * 2";
state.AcceptChanges();
```

## Tracking QueryView

```c#
[ChangeTracking]
public partial class State
{
    public partial ChangeTrackingList<int> List { get; set; } = [];
}

Goods state = new();
using var tracker = new ChangeTracker(state);

tracker.OnChange(()=> Console.WriteLine("Query results changed"))
tracker.Watch(
    selector: x => x.List, 
    predicate: x => x > 3);

// no output, the added item does not meet the condition of being greater than 5.
state.Tags.Add(1)
tracker.AcceptChanges();

// output: 'Query results changed'
state.Tags.Add(4)
tracker.AcceptChanges();

```

## Merge Changes

Some times we need to merge all changes, 
you can use `OnChange`

```c#

MyState state = new MyState();
var tracker = state.CreateTracker();

tracker.Watch(x => x.Count);
tracker.Watch(x => x.Price);

tracker.OnChange(state =>
{
    Console.WriteLine($"Count or Price has changed. Count={count}, Price={state.Price}");
});

//ouput: Count or Price has changed
state.Price = 3.14;
state.Count = 10;
state.AcceptChanges();

//no output, because Count has not changed
state.Count = 10;
state.AcceptChanges();

//no output, because property Number has not subscribed 
state.Number = 3;
state.AcceptChanges();

//ouput: Count or Price has changed
state.Count = 11;
state.AcceptChanges();
```

## DependencyInjection

The State class only has a parameterless constructor, making it easy to use dependency injection.

```c#

[ChangeTracking]
public partial class MyState(ILogger<MyState> logger) : State<MyState>
{
    public partial int Count { get; set; }

    public void Increment()
    {
        Count++;
        State.AcceptChanges();
        logger.LogInformation("Count Increment");
    }
}

var services = new ServiceCollection();
services.AddLogging();
services.AddScoped<Goods>();
services.AddSingleton<MyState>();
```

## Dispose & Unsubscribe

In most usage scenarios, when your page or component subscribes to the state, it must explicitly unsubscribe when the component is destroyed, otherwise it will result in a significant resource consumption.

```c#
Goods state = new();
var tracker = state.CreateTracker();
var disposable1 = state.Watch(x => x.Count);
var disposable2 = state.Watch(x => x.Tags.Count);
var disposable3 = state.OnChange(x => { });

disposable1.Dispose(); // unsubscribe: Count property watch
disposable2.Dispose(); // unsubscribe: Tags.Count property watch
disposable3.Dispose(); // unsubscribe: merge changed subscribe
tracker.Dispose(); // dispose tracker
```

## Use in Blazor 

You can use `States` in `Blazor`, it supports `AOT` compilation

```c#
//WebAssembly or Hybird
services.AddSingleton<Goods>();

//Server
services.AddScoped<Goods>();
```

**Inject state into component**
```razor
@inject Goods State
@implements IDisposable

<h1>Count: @State.Count</h1>
<button @onclick="Click">Add</button>

@code{
    private IChangeTracker Tracker;

    protected override void OnInitialized()
    {
        Tracker = State.CreateTracker();
        Tracker.Watch(x => x.Count);
        Tracker.OnChange(StateHasChanged);
    }

    private void Click()
    {
        State.Count++;
        State.AcceptChanges();
    }

    public void Dispose()
    {
        Tracker.Dispose();
    }
}
```

You can use the SourceGeneration.Blazor library to simplify this process, more information see [**Blazor.State**]([https://github.com/SourceGeneration/Blazor](https://github.com/hi-xiaoxin/Blazor.State)) repo

[![NuGet](https://img.shields.io/nuget/vpre/SourceGeneration.Blazor.State.svg)](https://www.nuget.org/packages/SourceGeneration.Blazor.State)

```c#
@inherits StateComponentBase
@inject Goods State

<h1>Count: @State.Count</h1>
<button @onclick="Click">Add</button>

@code{
    private int Count;

    protected override void OnStateBinding()
    {
        Watch(State, x => x.Count);
    }

    private void Click()
    {
        State.Count++;
        State.AcceptChanges();
    }
}

```
