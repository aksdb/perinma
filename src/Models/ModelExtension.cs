using System.Collections.Generic;

namespace perinma.Models;

public record ModelExtension<T>;

public class ModelExtensions
{
    private readonly Dictionary<object, object> _valueByExtension = [];

    public void Set<T>(ModelExtension<T> extension, T value) =>
        _valueByExtension[extension] = value!;

    public T? Get<T>(ModelExtension<T> extension) =>
        _valueByExtension.TryGetValue(extension, out var v) ? (T)v : default;
}
