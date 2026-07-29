using System.Collections;
using System.Reflection;

namespace CiyuanSha.GameCore.Tests.Framework;

[AttributeUsage(AttributeTargets.Method)]
public sealed class FactAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TheoryAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class InlineDataAttribute : Attribute
{
    public InlineDataAttribute(params object[] data) => Data = data;

    public object[] Data { get; }
}

public static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new TestFailureException(message ?? "Expected true but was false.");
        }
    }

    public static void False(bool condition, string? message = null) => True(!condition, message ?? "Expected false but was true.");

    public static void Equal<T>(T expected, T actual)
    {
        if (expected is IEnumerable expectedEnumerable && actual is IEnumerable actualEnumerable && expected is not string)
        {
            object?[] left = expectedEnumerable.Cast<object?>().ToArray();
            object?[] right = actualEnumerable.Cast<object?>().ToArray();
            if (!left.SequenceEqual(right))
            {
                throw new TestFailureException($"Sequences differ. Expected [{string.Join(", ", left)}], actual [{string.Join(", ", right)}].");
            }
            return;
        }

        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new TestFailureException($"Expected '{expected}', actual '{actual}'.");
        }
    }

    public static void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        T[] left = expected.ToArray();
        T[] right = actual.ToArray();
        if (!left.SequenceEqual(right))
        {
            throw new TestFailureException($"Sequences differ. Expected [{string.Join(", ", left)}], actual [{string.Join(", ", right)}].");
        }
    }

    public static void NotEqual<T>(T notExpected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
        {
            throw new TestFailureException($"Did not expect '{actual}'.");
        }
    }

    public static T NotNull<T>(T? value) where T : class => value ?? throw new TestFailureException("Expected non-null value.");

    public static void Empty(IEnumerable values)
    {
        if (values.Cast<object?>().Any())
        {
            throw new TestFailureException("Expected an empty sequence.");
        }
    }

    public static T Single<T>(IEnumerable<T> values)
    {
        T[] array = values.ToArray();
        if (array.Length != 1)
        {
            throw new TestFailureException($"Expected one item, actual {array.Length}.");
        }
        return array[0];
    }

    public static void All<T>(IEnumerable<T> values, Action<T> assertion)
    {
        foreach (T value in values)
        {
            assertion(value);
        }
    }

    public static TException Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new TestFailureException($"Expected {typeof(TException).Name}, got {exception.GetType().Name}.", exception);
        }
        throw new TestFailureException($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new TestFailureException($"Expected {typeof(TException).Name}, got {exception.GetType().Name}.", exception);
        }
        throw new TestFailureException($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }

    public static void DoesNotContain<T>(T value, IEnumerable<T> values)
    {
        if (values.Contains(value))
        {
            throw new TestFailureException($"Sequence unexpectedly contains '{value}'.");
        }
    }
}

public sealed class TestFailureException : Exception
{
    public TestFailureException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public static class TestRunner
{
    public static async Task<int> RunAsync(Assembly assembly)
    {
        int passed = 0;
        int failed = 0;
        IEnumerable<MethodInfo> methods = assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<FactAttribute>() is not null || method.GetCustomAttribute<TheoryAttribute>() is not null)
            .OrderBy(method => method.DeclaringType!.FullName, StringComparer.Ordinal)
            .ThenBy(method => method.Name, StringComparer.Ordinal);

        foreach (MethodInfo method in methods)
        {
            InlineDataAttribute[] data = method.GetCustomAttributes<InlineDataAttribute>().ToArray();
            IEnumerable<object[]> cases = data.Length == 0 ? new[] { Array.Empty<object>() } : data.Select(item => item.Data);
            int caseIndex = 0;
            foreach (object[] arguments in cases)
            {
                string name = $"{method.DeclaringType!.Name}.{method.Name}" + (data.Length > 0 ? $"[{caseIndex++}]" : string.Empty);
                try
                {
                    object instance = Activator.CreateInstance(method.DeclaringType!)!;
                    object? result = method.Invoke(instance, arguments);
                    if (result is Task task)
                    {
                        await task;
                    }
                    Console.WriteLine($"PASS {name}");
                    passed++;
                }
                catch (Exception exception)
                {
                    Exception actual = exception is TargetInvocationException { InnerException: not null } ? exception.InnerException : exception;
                    Console.Error.WriteLine($"FAIL {name}: {actual.Message}");
                    Console.Error.WriteLine(actual.StackTrace);
                    failed++;
                }
            }
        }

        Console.WriteLine($"RESULT passed={passed} failed={failed}");
        return failed == 0 ? 0 : 1;
    }
}
