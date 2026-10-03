using System.Collections;
using System.Data;
using System.Data.Common;
using Xunit;

namespace BricksNet.AdoNet;

public sealed class BricksNetParameterTests
{
    [Fact]
    public void Constructor_SetsNameAndValue()
    {
        var parameter = new BricksNetParameter("@p", 42);

        Assert.Equal("@p", parameter.ParameterName);
        Assert.Equal(42, parameter.Value);
        Assert.Equal(ParameterDirection.Input, parameter.Direction);
        Assert.Equal(DbType.Int32, parameter.DbType);
    }

    [Fact]
    public void Constructor_NullName_BecomesEmpty()
    {
        var parameter = new BricksNetParameter(null!, null);

        Assert.Equal(string.Empty, parameter.ParameterName);
        Assert.Null(parameter.Value);
    }

    [Fact]
    public void Direction_Input_IsAccepted()
    {
        var parameter = new BricksNetParameter { Direction = ParameterDirection.Input };

        Assert.Equal(ParameterDirection.Input, parameter.Direction);
    }

    [Theory]
    [InlineData(ParameterDirection.Output)]
    [InlineData(ParameterDirection.InputOutput)]
    [InlineData(ParameterDirection.ReturnValue)]
    public void Direction_NonInput_Throws(ParameterDirection direction)
    {
        var parameter = new BricksNetParameter();

        Assert.Throws<NotSupportedException>(() => parameter.Direction = direction);
    }

    [Fact]
    public void ParameterName_Null_BecomesEmpty()
    {
        var parameter = new BricksNetParameter { ParameterName = "x" };
        parameter.ParameterName = null;

        Assert.Equal(string.Empty, parameter.ParameterName);
    }

    [Fact]
    public void SourceColumn_Null_BecomesEmpty()
    {
        var parameter = new BricksNetParameter { SourceColumn = "col" };
        Assert.Equal("col", parameter.SourceColumn);

        parameter.SourceColumn = null;
        Assert.Equal(string.Empty, parameter.SourceColumn);
    }

    [Fact]
    public void ResetDbType_RestoresStringDefault()
    {
        var parameter = new BricksNetParameter { DbType = DbType.Int32 };

        parameter.ResetDbType();

        Assert.Equal(DbType.String, parameter.DbType);
    }

    [Fact]
    public void ScalarProperties_RoundTrip()
    {
        var parameter = new BricksNetParameter
        {
            IsNullable = true,
            Size = 10,
            SourceColumnNullMapping = true,
            TypeHint = "INT",
            Value = "v",
        };

        Assert.True(parameter.IsNullable);
        Assert.Equal(10, parameter.Size);
        Assert.True(parameter.SourceColumnNullMapping);
        Assert.Equal("INT", parameter.TypeHint);
        Assert.Equal("v", parameter.Value);
    }
}

public sealed class BricksNetParameterCollectionTests
{
    private static BricksNetParameter P(string name, object? value = null) => new(name, value);

    [Fact]
    public void Add_AppendsAndReturnsIndex()
    {
        var collection = new BricksNetParameterCollection();

        var index0 = collection.Add(P("@a"));
        var index1 = collection.Add(P("@b"));

        Assert.Equal(0, index0);
        Assert.Equal(1, index1);
        Assert.Equal(2, collection.Count);
    }

    [Fact]
    public void Add_NonParameter_Throws()
    {
        var collection = new BricksNetParameterCollection();

        Assert.Throws<InvalidCastException>(() => collection.Add("not a parameter"));
    }

    [Fact]
    public void Add_Null_Throws()
    {
        var collection = new BricksNetParameterCollection();

        Assert.Throws<ArgumentNullException>(() => collection.Add(null!));
    }

    [Fact]
    public void AddRange_AddsAll()
    {
        var collection = new BricksNetParameterCollection();

        collection.AddRange(new[] { P("@a"), P("@b"), P("@c") });

        Assert.Equal(3, collection.Count);
    }

    [Fact]
    public void AddRange_Null_Throws()
    {
        var collection = new BricksNetParameterCollection();

        Assert.Throws<ArgumentNullException>(() => collection.AddRange(null!));
    }

    [Fact]
    public void Contains_ByValueAndName()
    {
        var collection = new BricksNetParameterCollection();
        var param = P("@a");
        collection.Add(param);

        Assert.True(collection.Contains(param));
        Assert.True(collection.Contains("@A"));
        Assert.False(collection.Contains("@missing"));
    }

    [Fact]
    public void IndexOf_ByValueAndName()
    {
        var collection = new BricksNetParameterCollection();
        var first = P("@a");
        var second = P("@b");
        collection.Add(first);
        collection.Add(second);

        Assert.Equal(1, collection.IndexOf(second));
        Assert.Equal(0, collection.IndexOf("@a"));
        Assert.Equal(-1, collection.IndexOf("@nope"));
    }

    [Fact]
    public void Insert_PlacesAtIndex()
    {
        var collection = new BricksNetParameterCollection();
        collection.Add(P("@a"));
        collection.Insert(0, P("@z"));

        Assert.Equal("@z", ((DbParameter)collection[0]).ParameterName);
    }

    [Fact]
    public void Remove_And_RemoveAt()
    {
        var collection = new BricksNetParameterCollection();
        var a = P("@a");
        var b = P("@b");
        var c = P("@c");
        collection.Add(a);
        collection.Add(b);
        collection.Add(c);

        collection.Remove(b);
        Assert.Equal(2, collection.Count);

        collection.RemoveAt(0);
        Assert.Equal("@c", ((DbParameter)collection[0]).ParameterName);

        collection.RemoveAt("@c");
        Assert.Equal(0, collection.Count);
    }

    [Fact]
    public void RemoveAt_UnknownName_Throws()
    {
        var collection = new BricksNetParameterCollection();

        Assert.Throws<IndexOutOfRangeException>(() => collection.RemoveAt("@nope"));
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        var collection = new BricksNetParameterCollection();
        collection.Add(P("@a"));
        collection.Add(P("@b"));

        collection.Clear();

        Assert.Equal(0, collection.Count);
    }

    [Fact]
    public void Indexer_ByName_GetAndSet()
    {
        var collection = new BricksNetParameterCollection();
        collection.Add(P("@a", 1));

        Assert.Equal(1, ((DbParameter)collection["@a"]).Value);

        collection["@a"] = P("@a", 2);
        Assert.Equal(2, ((DbParameter)collection["@a"]).Value);

        // Setting an unknown name appends.
        collection["@b"] = P("@b", 3);
        Assert.Equal(2, collection.Count);
    }

    [Fact]
    public void Indexer_ByOrdinal_GetAndSet()
    {
        var collection = new BricksNetParameterCollection();
        collection.Add(P("@a", 1));

        collection[0] = P("@a", 9);
        Assert.Equal(9, ((DbParameter)collection[0]).Value);
    }

    [Fact]
    public void GetParameter_UnknownName_Throws()
    {
        var collection = new BricksNetParameterCollection();

        Assert.Throws<IndexOutOfRangeException>(() => _ = collection["@nope"]);
    }

    [Fact]
    public void CopyTo_And_Enumerator()
    {
        var collection = new BricksNetParameterCollection();
        collection.Add(P("@a"));
        collection.Add(P("@b"));

        var array = new BricksNetParameter[2];
        collection.CopyTo(array, 0);
        Assert.Equal("@a", array[0].ParameterName);

        var count = 0;
        foreach (var _ in collection)
        {
            count++;
        }

        Assert.Equal(2, count);
        Assert.NotNull(collection.SyncRoot);
    }
}
