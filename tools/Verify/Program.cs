using System.Reflection;
using Genesis.Engine.Core.Tests;

// Execute parameterless Facts without testhost's parent-process monitoring.
var assembly = typeof(CoreTests).Assembly;
var passed = 0;
foreach (var type in assembly.GetTypes())
foreach (var method in type.GetMethods().Where(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "FactAttribute")))
{
    if (method.GetParameters().Length != 0) throw new InvalidOperationException("Only parameterless Facts are supported.");
    try { method.Invoke(Activator.CreateInstance(type), null); passed++; Console.WriteLine($"PASS {type.Name}.{method.Name}"); }
    catch (TargetInvocationException exception) { Console.Error.WriteLine(exception.InnerException); return 1; }
}
var theories = assembly.GetTypes().SelectMany(t => t.GetMethods()).Count(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "TheoryAttribute"));
Console.WriteLine($"Passed {passed} Facts (direct runner; not VSTest). Skipped {theories} Theory methods: run dotnet test for the complete suite.");
return passed > 0 ? 0 : 1;
