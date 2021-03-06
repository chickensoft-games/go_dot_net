# GoDotNet

[![Discord](https://img.shields.io/badge/Chickensoft%20Discord-%237289DA.svg?style=flat&logo=discord&logoColor=white)][discord]

> Simple dependency injection, state management, serialization, and other utilities for C# Godot development.

GoDotNet aims to make well-structured C# code for your Godot game a reality.

While developing our own game, we couldn't find any simple C# solutions to solve problems like dependency injection, basic serialization of Godot objects, and state machines. So, we built our own mechanisms that were heavily inspired by other popular frameworks. Hopefully you can benefit from them, too!

For a full description of everything GoDotNet can do, read on!

Are you on discord? If you're building games with Godot and C#, we'd love to see you in the [Chickensoft Discord server][discord]!

## Installation

Find the latest version of [GoDotNet][go_dot_net_nuget] on nuget.

In your `*.csproj`, add the following snippet in your `<ItemGroup>`, save, and run `dotnet restore`. Make sure to replace `*VERSION*` with the latest version.

```xml
<PackageReference Include="Chickensoft.GoDotNet" Version="*VERSION*" />
```

GoDotNet is itself written in C# 10 for `netstandard2.1` (the highest language version currently supported by Godot). If you want to setup your project the same way, look no further than the [`GoDotNet.csproj`](GoDotNet.csproj) file for inspiration!

## Logging

Internally, GoDotNet uses [GoDotLog] for logging. GoDotLog allows you to easily create loggers that output nicely formatted, prefixed messages (in addition to asserts other exception-aware execution utilities).

## Extensions

GoDotNet provides a number of extensions on common Godot objects.
### Godot Collections ↔️ Dotnet Collections

A Godot Dictionary or Array can be converted to a .NET collection easily in `O(n)` time. Certain [bugs][godot-dictionary-iterable-issue] in Godot's collection types necessitate the need for converting back-and-forth occasionally. For performance reasons, try to avoid doing this often on large collections.

```csharp
using Godot.Collections;

var array = new Array().ToDotNet();
var dictionary = new Dictionary<KeyType, ValueType>().ToDotNet();
```

### Node Utilities

#### Autoloads

An autoload can be fetched from any node:

```csharp
public class MyEntity : Node {
  private MyAutoloadType _myAutoload = this.Autoload<MyAutoloadType>();

  public override void _Ready() {
    _myAutoload.DoSomething();
  }
}
```

#### Dependencies

Nodes can indicate that they require a certain type of value to be provided to them by any ancestor node above them in the scene tree.

GoDotNet's dependency system is [discussed in detail below](#dependency-injection).

To fetch a dependency:

```csharp
// Inside a Node subclass:
[Dependency]
private MyDependencyType _myDependency => this.DependOn<MyDependencyType>();
```

## Scheduling

A `Scheduler` node is included which allows callbacks to be run on the next frame, similar to [CallDeferred][call-deferred]. Unlike `CallDeferred`, the scheduler uses vanilla C# to avoid marshalling types to Godot. Since Godot cannot marshal objects that don't extend `Godot.Object`/`Godot.Reference`, this utility is provided to perform the same function for records, custom types, and C# collections which otherwise couldn't be marshaled between C# and Godot.

Create a new autoload which extends the scheduler:

```csharp
using GoDotNet;

public class GameScheduler : Scheduler { }
```

Add it to your `project.godot` file (preferably the first entry):

```ini
[autoload]

GameScheduler="*res://autoload_folder/GameScheduler.cs"
```

...and simply schedule a callback to run on the next frame:

```csharp
this.Autoload<Scheduler>().NextFrame(
  () => _log.Print("I won't execute until the next frame.")
)
```

## Dependency Injection

GoDotNet provides a simple dependency injection system which allows dependencies to be provided to child nodes, looked-up, cached, and read on demand. By "dependency", we simply mean "any value or instance a node might need to perform its job." Oftentimes, dependencies are simply instances of custom classes which perform game logic.

### Why have a dependency system?

Why are dependency injection systems helpful? In Godot, providing values to descendent nodes typically requires parent nodes to pass values to children nodes via method calls, following the ["call down, signal upwards"][call-down-signal-up] architecture rule. This creates a tight coupling between the parent and the child since the parent has to know which children need which values.

If a distant descendant node of the parent also needs the same value, the parent's children have to pass it down until it reaches the correct descendent, too. Not only is it an awful lot of typing to create all those methods which just pass an object to a child node, it makes the code harder to follow as you may have to trace the dependency through many different files. All of this reinforces tight coupling, too, which is exactly what a good dependency injection system should prevent.

Finally, doing all that work doesn't even guarantee that the descendants will have the most up-to-date value or instance of the thing they need.

GoDotNet's dependency system solves this by letting nodes indicate they provide values by implementing an interface, and letting the descendent nodes that need those values look for nodes above them that provide the values they need. GoDotNet takes care of all the work of caching dependency providers and announcing when dependencies are ready to be used under the hood.

Dependent nodes don't need to know what kind of ancestor nodes they have — they just search for the closest node above them that can provide the value they need, ensuring loose coupling in both directions.

### Providing and Fetching Dependencies

Providing values to nodes further down the tree is based on the idea of scoping dependencies, inspired by [popular systems][provider] in other frameworks that have already demonstrated their usefulness.

To create a node which provides a value to all of its descendant nodes, you must implement the `IProvider<T>` interface.

`IProvider` requires a single `Get()` method that returns an instance of the object it provides.

```csharp
public class MySceneNode : IProvider<MyObject> {
  // If this object has to be created in _Ready(), we can use `null!` since we
  // know the value will be valid after _Ready is called. This is as close as we
  // can get to the `late` modifier in Dart or `lateinit` in Kotlin.
  private MyObject _object = null!;

  // IProvider<MyObject> requires us to implement a single method:
  MyObject IProvider<MyObject>.Get() => _object;

  public override void _Ready() {
    _object = new MyObject();
    
    // Notify any dependencies that the values provided are now available.
    this.Provided();
  }
}
```

Once all of the values are initialized, the provider node must call `this.Provided()` to inform any dependencies that the provided values are now available. Any dependencies already in existence in the subtree will have their `Loaded()` methods called, allowing them to perform initialization with the now-available dependencies.

Providers should only call `this.Provided()` after all of the values provided are non-null.

Dependent nodes that are added after the provider node has initialized their dependencies will have their `Loaded()` method called right away.


> `this.Provided()` is necessary because `_Ready()` is called on child nodes *before* parent nodes due to [Godot's tree order][godot-tree-order]. If you try to use a dependency in a dependent node's `_Ready()` method, there's no guarantee that it's been created, which results in null exception errors. Since it's often not possible to create dependencies until `_Ready()`, provider nodes are expected to invoke `this.Provided()` once all of their provided values are created.

Nodes can provide multiple values just as easily.

```csharp
public class MySceneNode : IProvider<MyObject>, IProvider<MyOtherObject> {
  private MyObject _object = null!;

  private MyOtherObject _otherObject = null!;

  MyObject IProvider<MyObject>.Get() => _object;
  MyOtherObject IProvider<MyOtherObject>.Get() => _otherObject;

  public override void _Ready() {
    _object = new MyObject(/* ... */);
    _otherObject = new MyOtherObject(/* ... */);

    // Notify any dependencies that the values provided are now available.
    this.Provided();
  }
}
```

To use dependencies, a node must implement `IDependent` and call `this.Depend()` at the end of the `_Ready()` method.

Dependent nodes declare dependencies by creating a property with the `[Dependency]` attribute and calling the node extension method `this.DependOn` with the type of value they are depending on.

```csharp
[Dependency]
private ObjectA _a => this.DependOn<ObjectA>();

[Dependency]
private ObjectB _b => this.DependOn<ObjectB>();
```

The `IDependent` interface requires you to implement a single void method, `Loaded()`, which is called once all the values the node depends on have been initialized by their providers. For `Loaded()` to be called, you must call `this.Depend()` in your dependent node's `_Ready()` method.

```csharp
public void Loaded() {
  // _a and _b are guaranteed to be non-null here.
  _a.DoSomething();
  _b.DoSomething();
}
```

> Internally, `this.Depend()` will look up all of the properties of your node which have a `[Dependency]` attribute and cache their providers for future access. If a provider hasn't initialized a dependency, hooks will be registered which call your dependent node's `Loaded()` method once all the dependencies are available. 

 In `Loaded()`, dependent nodes are guaranteed to be able to access their dependency values. Below is a complete example.

```csharp
public class DependentNode : Node, IDependent {
  // As long as there's a node which implements IProvider<MyObject> above us,
  // we will be able to access this object once `Loaded()` is called.
  [Dependency]
  private MyObject _object => this.DependOn<MyObject>();

  public override void _Ready() {
    // _object might actually be null here if the parent provider doesn't create
    // it in its constructor. Since many providers won't be creating 
    // dependencies until their _Ready() is invoked, which happens *after*
    // child node, we shouldn't reference dependencies in dependent nodes'
    // _Ready() methods.

    this.Depend();
  }

  public void Loaded() {
    // This method is called by the dependency system when all of the provided
    // values we depend on have been finalized by their providers!
    //
    // _object is guaranteed to be initialized here!
    _object.DoSomething();
  }
}
```

*Note*: If the dependency system can't find the correct provider in a dependent node's ancestors, it will search all of the autoloads for an autoload which implements the correct provider type. This allows you to "fallback" to global providers (should you want to).

### Dependency Caveats

Like all dependency injection systems, there are a few corner cases you should be aware of.

#### Removing and Re-adding Nodes

If a node is removed from the tree and inserted somewhere else in the tree, it might try to use a cached version of the wrong provider. To prevent invalid
situations like this, you should clear the dependency cache and recreate it when a node re-enters the tree. This can be accomplished by simply calling `this.Depend()` again from the dependent node, which will call `Loaded()` again.

By placing provider nodes above all the possible parents of a node which depends on that value, you can ensure that a node will always be able to find the dependency it requests. Clever provider hierarchies will prevent most of these headaches.

#### Dependency Deadlock

If you initialize dependencies in a complex (or slow way) by failing to call `this.Provided()` from your provider's `_Ready()` method, there is a risk of seriously slowing down (or deadlocking) the dependency resolution in the children. `Loaded()` isn't called on child nodes using `this.Depend()` until **all** of the dependencies they depend on from the ancestor nodes have been provided, so `Loaded()` will only be invoked when the slowest dependency has been marked provided via `this.Provided()` in the ancestor provider node.

To avoid this situation entirely, always initialize dependencies in your provider's `_Ready()` method and call `this.Provided()` immediately afterwards.

## State Machines

GoDotNet provides a simple state machine implementation that emits a C# event when the state changes (since [Godot signals are more fragile](#signals-and-events)). If you try to update the machine to a state that isn't a valid transition from the current state, it throws an exception. The machine requires that an initial state be given when the machine is constructed to avoid nullability issues.

State machines are not extensible — instead, GoDotNet almost always prefers the pattern of [composition over inheritance][composition-inheritance]. The state machine relies on state equality to determine if the state has changed to avoid issuing unnecessary events. Using `record` types for the state allows this to happen automatically. 

States used with a state machine must implement `IMachineState<T>`, where T is just the type of the machine state. Otherwise, the default implementation returns `true` to allow transitions to any state.

States can optionally implement `CanTransitionTo(IMachineState state)` to determine if the proposed state transition is valid.

```csharp
public interface IGameState : IMachineState<IGameState> { }

public record GameMainMenuState : IGameState {
  public bool CanTransitionTo(IGameState state) => state is GameLoadingState;
}

public record GameLoadingState : IGameState {
  public bool CanTransitionTo(IGameState state) => state is GamePlayingState;
}

// States can store values!
public record GamePlayingState(string PlayerName) {
  public bool CanTransitionTo(IGameState state) => state is GameMainMenuState;
}
```

Machines are fairly simple to use: create one with an initial state (and optionally register a machine state change event handler). A state machine will announce the state has changed as soon as it is constructed.

```csharp
public class GameManager : Node {
  private readonly Machine<IGameState> _machine;

  // Expose the machine's event.
  public event Machine<IGameState>.Changed OnChanged {
    add => _machine.OnChanged += value;
    remove => _machine.OnChanged -= value;
  }

  public override void _Ready() {
    _machine = new Machine<IGameState>(new GameMainMenuState(), onChanged);
  }

  /// <summary>Starts the game.</summary>
  public void Start(string name) {
    _machine.Update(new GameLoadingState());
    // do your loading...
    // ...
    // start the game!
    _machine.Update(new GamePlayingState(name);
  }

  /// <summary>Goes back to the menu.</summary>
  public void GoToMenu() => _machine.Update(new GameMainMenuState());

  public void OnChanged(IGameState state) {
    if (state is GamePlayingState playingState) {
      var playerName = playingState.Name();
      // ...
    }
  }
}
```

## Notifiers

A notifier is an object which emits a signal when its value changes. Notifiers are similar to state machines, but they don't care about transitions. Any update that changes the value (determined by comparing the new value with the previous value using `Object.Equals`) will emit a signal. It's often convenient to use record types as the value of a Notifier. Like state machines, the value of a notifier can never be `null` — make sure you initialize with a valid value!

Because notifiers check equality to determine changes, they are convenient to use with value types (like primitives and structs). Notifiers, like state machines, also emit a signal to announce their value as soon as they are constructed.

```csharp
private var _notifier
  = new Notifier<string>("Player", OnPlayerNameChanged);

private void OnPlayerNameChanged(string name) {
  _log.Print($"Player name changed to $name");
}
```

Like state machines, notifiers should typically be kept private. Instead of letting consumers modify the value directly, you can create a manager class which provides methods that mutate the notifier value. These manager classes can provide an event which redirects to the notifier event, or they can emit their own events when certain pieces of the notifier value changes.

```csharp
public record EnemyData(string Name, int Health);

public class EnemyManager {
  private readonly Notifier<EnemyData> _notifier;

  public event Notifier<EnemyData>.Changed OnChanged {
    add => _notifier.OnChanged += value;
    remove => _notifier.OnChanged -= value;
  }

  public EnemyData Value => _notifier.Value;

  public EnemyManager(string name, int health) => _notifier = new(
    new EnemyData(name, health)
  );

  public void UpdateName(string name) =>
    _notifier.Value = _notifier.Value with { Name = name };

  public void UpdateHealth(int health) =>
    _notifier.Value = _notifier.Value with { Health = health };
}
```

The class above shows an enemy manager which manages a single enemy's state and emits an `OnChanged` event whenever any part of the enemy's state changes. You can easily modify it to emit more specific events when certain pieces of the enemy state changes.

```csharp
public class EnemyManager {
  private readonly Notifier<EnemyData> _notifier;

  public EnemyData Value => _notifier.Value;

  public event Action<string>? OnNameChanged;
  public event Action<int>? OnHealthChanged;

  public EnemyManager(string name, int health) => _notifier = new(
    new EnemyData(name, health),
    OnChanged
  );

  public void UpdateName(string name) =>
    _notifier.Value = _notifier.Value with { Name = name };

  public void UpdateHealth(int health) =>
    _notifier.Value = _notifier.Value with { Health = health };

  private void OnChanged(EnemyData enemy, EnemyData? previous) {
    // Emit different events depending on what changed.
    if (!System.Object.Equals(enemy.Name, previous?.Name)) {
      OnNameChanged?.Invoke(enemy.Name);
    }
    else if (!System.Object.Equals(enemy.Health, previous?.Health)) {
      OnHealthChanged?.Invoke(enemy.Health);
    }
  }
}
```

By providing manager classes which wrap state machines or notifiers to dependent nodes, you can create nodes which easily respond to changes in the values provided by distant ancestor nodes.

## Serialization of Godot Objects

GoDotNet provides a `GDObject` which extends `Godot.Reference` (which is itself a `Godot.Object`). Because it is a `Godot.Object`, `GDObject` can be marshalled back and forth between Godot's C++ layer without any issue, provided each of its fields is also a type that Godot supports.

GoDotNet takes this a step further and offers basic dictionary serialization for GDObjects using the popular `Newtonsoft.Json` package (if you use the appropriate annotations for constructors and fields).

When extending `GDObject` or `Godot.Object`, it is imperative to offer a default constructor with no parameters. Godot uses this default constructor [to create a default value for exported fields][export-default-values] in the editor.

```csharp
public class MyObject : GDObject {

  // Init properties are a great way to keep things immutable on GDObject.
  // Marking this with [JsonProperty] allows it to be serialized using 
  // Newtonsoft.Json.
  [JsonProperty]
  public string PlayerName { get; init; }

  // Default constructor to keep Godot happy.
  public MyObject() {
    PlayerName = default;
  }

  // Tell Newtonsoft.Json to use this constructor for deserialization.
  [JsonConstructor]
  public MyObject(string playerName) {
    PlayerName = playerName;
  }
}
```

To serialize and deserialize a `GDObject` to and from a dictionary, respectively, you can use the static methods on `GDObject.`

```csharp
var alice = new MyObject("Alice");
Dictionary<string, object?> data = GDObject.ToData(alice);
MyObject aliceAgain = GDObject.FromData<MyObject>(data);
```

Likewise, if you are convinced each key and value in the `GDObject` is string convertible, you can serialize the model to a `Dictionary<string, string>`. This is helpful when passing data around for Steamworks lobbies, for example.

```csharp
var bob = new MyObject("Bob");
Dictionary<string, string> stringData = GDObject.ToStringData(bob);
MyObject bobAgain = GDObject.FromStringData(stringData);
```

## Technical Challenges

### Signals and Events

Godot supports emitting [signals] from C#. Because Godot signals pass through the Godot engine, any arguments given to the signal must be marshalled through Godot, forcing them to be classes which extend `Godot.Object`/`Godot.Reference` (records aren't allowed). Likewise, all the fields in the class must also be the same kind of types so they can be marshalled, and so on.

It's not possible to have static typing with signal parameters, so you don't find out until runtime if you accidentally passed the wrong parameter. The closest you can do is the following, which wouldn't break at compile time if the receiving function signature happened to be wrong.

```csharp
public class ObjectType {
  [Signal]
  public delegate void DoSomething(string value); 
}

public class MyNode : Node {
  // Inside your node
  public override void _Ready() {
    _ = object.Connect(
      nameof(ObjectType.DoSomething),
      this,
      nameof(MyDoSomething)
    );
  }

  private void DoSomething(int value) {
    // WARNING: Value should be string, not int!!
  }
}
```

Because of these limitations, GoDotNet will avoid Godot signals except when necessary to interact with Godot components. For communication between C# game logic, it will typically be preferable to use C# events instead.

```csharp
// Declare an event signature — no [Signal] attribute necessary.
public delegate void Changed(Type1 value1, Type2 value2);

// Add an event field to your emitter
public event Changed? OnChanged;

// Trigger an event from your emitter:
OnChanged?.Invoke(argument1, argument2);

// Listen to an event in your receiver:
var emitter = new MyEmitterObject();
emitter.OnChanged += MyOnChangedHandler;

// Event handler in your receiver:
private void MyOnChangedHandler(Type1 value1, Type2 value2) {
  // respond to event we received
}
```

<!-- References -->

[discord]: https://discord.gg/gSjaPgMmYW
[net-5-0]: https://github.com/godotengine/godot/issues/43458#issuecomment-725550284
[go_dot_net_nuget]: https://www.nuget.org/packages/Chickensoft.GoDotNet/
[GoDotLog]: https://github.com/chickensoft-games/go_dot_log
[godot-dictionary-iterable-issue]: https://github.com/godotengine/godot/issues/56733
[call-deferred]: https://docs.godotengine.org/en/stable/classes/class_object.html#class-object-method-call-deferred
[provider]: https://pub.dev/packages/provider
[call-down-signal-up]: https://kidscancode.org/godot_recipes/basics/node_communication/
[signals]: https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_features.html#c-signals
[composition-inheritance]: https://en.wikipedia.org/wiki/Composition_over_inheritance
[export-default-values]: https://github.com/godotengine/godot/issues/37703#issuecomment-877406433
[godot-tree-order]: https://kidscancode.org/godot_recipes/basics/tree_ready_order/
