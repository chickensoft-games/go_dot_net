using System.Reflection;
using Godot;
using GoDotTest;

public class Main : Node2D {
  public override async void _Ready()
    => await GoTest.RunTests(Assembly.GetExecutingAssembly(), this);
}
