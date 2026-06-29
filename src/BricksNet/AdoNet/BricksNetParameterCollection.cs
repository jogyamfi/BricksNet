using System.Collections;
using System.Data.Common;

namespace BricksNet.AdoNet;

/// <summary>
/// A collection of <see cref="BricksNetParameter"/> instances owned by a <see cref="BricksNetCommand"/>.
/// </summary>
public sealed class BricksNetParameterCollection : DbParameterCollection
{
    private readonly List<BricksNetParameter> _parameters = [];
    private readonly object _syncRoot = new();

    /// <inheritdoc />
    public override int Count => _parameters.Count;

    /// <inheritdoc />
    public override object SyncRoot => _syncRoot;

    /// <inheritdoc />
    public override int Add(object value)
    {
        _parameters.Add(Cast(value));
        return _parameters.Count - 1;
    }

    /// <inheritdoc />
    public override void AddRange(Array values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var value in values)
        {
            _parameters.Add(Cast(value));
        }
    }

    /// <inheritdoc />
    public override void Clear() => _parameters.Clear();

    /// <inheritdoc />
    public override bool Contains(object value) => _parameters.Contains(Cast(value));

    /// <inheritdoc />
    public override bool Contains(string value) => IndexOf(value) >= 0;

    /// <inheritdoc />
    public override void CopyTo(Array array, int index) =>
        ((ICollection)_parameters).CopyTo(array, index);

    /// <inheritdoc />
    public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();

    /// <inheritdoc />
    public override int IndexOf(object value) => _parameters.IndexOf(Cast(value));

    /// <inheritdoc />
    public override int IndexOf(string parameterName)
    {
        for (var i = 0; i < _parameters.Count; i++)
        {
            if (string.Equals(_parameters[i].ParameterName, parameterName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <inheritdoc />
    public override void Insert(int index, object value) => _parameters.Insert(index, Cast(value));

    /// <inheritdoc />
    public override void Remove(object value) => _parameters.Remove(Cast(value));

    /// <inheritdoc />
    public override void RemoveAt(int index) => _parameters.RemoveAt(index);

    /// <inheritdoc />
    public override void RemoveAt(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index < 0)
        {
            throw new IndexOutOfRangeException($"No parameter named '{parameterName}' was found.");
        }

        _parameters.RemoveAt(index);
    }

    /// <inheritdoc />
    protected override DbParameter GetParameter(int index) => _parameters[index];

    /// <inheritdoc />
    protected override DbParameter GetParameter(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index < 0)
        {
            throw new IndexOutOfRangeException($"No parameter named '{parameterName}' was found.");
        }

        return _parameters[index];
    }

    /// <inheritdoc />
    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = Cast(value);

    /// <inheritdoc />
    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var index = IndexOf(parameterName);
        if (index < 0)
        {
            _parameters.Add(Cast(value));
        }
        else
        {
            _parameters[index] = Cast(value);
        }
    }

    private static BricksNetParameter Cast(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value as BricksNetParameter
            ?? throw new InvalidCastException(
                $"Only '{nameof(BricksNetParameter)}' instances may be added to a '{nameof(BricksNetParameterCollection)}'.");
    }
}
