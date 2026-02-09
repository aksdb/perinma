using System;

namespace perinma.Utils;

public static class ExtensionFunctions
{
    public static TResult Let<T, TResult>(this T value, Func<T, TResult> func) =>
        func(value);
}
