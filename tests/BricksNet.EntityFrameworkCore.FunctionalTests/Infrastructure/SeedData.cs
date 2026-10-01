namespace BricksNet.EntityFrameworkCore.FunctionalTests.Infrastructure;

/// <summary>Deterministic seed data, chosen to exercise edge cases of each translation.</summary>
public static class SeedData
{
    private static readonly string[] Cities = ["Oslo", "Lagos", "Accra", "Lima", "Kyoto"];
    private static readonly string[] Statuses = ["new", "paid", "shipped", "cancelled"];

    public static IReadOnlyList<Customer> Customers()
    {
        var customers = new List<Customer>();
        var orderId = 1;
        for (var id = 1; id <= 20; id++)
        {
            var customer = new Customer
            {
                Id = id,
                Name = $"Customer {id:D2}",
                City = id % 6 == 0 ? null : Cities[id % Cities.Length],
                IsActive = id % 3 != 0,
                Address = new Address { Street = $"{id} Main St", PostCode = id % 4 == 0 ? null : $"P{id}" },
            };

            // Customers 19 and 20 have no orders, to exercise outer joins and empty groups.
            var orderCount = id >= 19 ? 0 : (id % 5) + 1;
            for (var n = 0; n < orderCount; n++, orderId++)
            {
                customer.Orders.Add(new Order
                {
                    Id = orderId,
                    CustomerId = id,
                    Amount = Math.Round((orderId * 13.37m) % 250, 2),
                    Quantity = (orderId % 7) + 1,
                    OrderDate = new DateOnly(2026, 1, 1).AddDays(orderId * 3),
                    Status = Statuses[orderId % Statuses.Length],
                });
            }

            customers.Add(customer);
        }

        return customers;
    }

    public static IReadOnlyList<Sample> Samples() =>
    [
        new() { Id = 1, Text = "hello world", Note = null, Number = 7, Value = 2.5, Amount = 2.5m, Flag = true, At = new DateTime(2026, 3, 4, 5, 6, 7), Day = new DateOnly(2026, 3, 4), Stamp = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero) },
        new() { Id = 2, Text = "  padded  ", Note = "   ", Number = -3, Value = 3.5, Amount = -2.5m, Flag = false, At = new DateTime(2024, 2, 29, 23, 59, 59), Day = new DateOnly(2024, 2, 29), Stamp = new DateTimeOffset(2024, 2, 29, 23, 59, 59, TimeSpan.Zero) },
        new() { Id = 3, Text = "it's \"quoted\" \\ back", Note = "note", Number = 0, Value = -2.5, Amount = 0.005m, Flag = true, At = new DateTime(2000, 1, 1, 0, 0, 0), Day = new DateOnly(2000, 1, 1), Stamp = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero) },
        new() { Id = 4, Text = "abcabc", Note = "", Number = 100, Value = 0.1, Amount = 123.456m, Flag = false, At = new DateTime(2026, 12, 31, 12, 0, 0), Day = new DateOnly(2026, 12, 31), Stamp = new DateTimeOffset(2026, 12, 31, 12, 0, 0, TimeSpan.Zero) },
        new() { Id = 5, Text = "Mixed Case", Note = "x", Number = 42, Value = 1e10, Amount = 99.995m, Flag = true, At = new DateTime(2026, 6, 15, 18, 30, 0), Day = new DateOnly(2026, 6, 15), Stamp = new DateTimeOffset(2026, 6, 15, 18, 30, 0, TimeSpan.Zero) },
    ];

    public static IReadOnlyList<TypeRow> TypeRows() =>
    [
        new()
        {
            Id = 1, Bool = true, SByte = 5, Byte = 200, Short = 300, UShort = 60000, UInt = 4_000_000_000, Long = 9_000_000_000,
            ULong = 18_000_000_000_000_000_000, Float = 1.5f, Double = 2.25, Decimal = 1234.5678m, Text = "plain", Char = 'Z',
            Date = new DateOnly(2026, 3, 4), DateTime = new DateTime(2026, 3, 4, 5, 6, 7).AddTicks(1_234_560),
            DateTimeOffset = new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.FromHours(2)), TimeOnly = new TimeOnly(13, 45, 30),
            TimeSpan = new TimeSpan(1, 2, 3, 4), Bytes = [1, 2, 3], Guid = Guid.Parse("11112222-3333-4444-5555-666677778888"),
            NullableInt = 7, NullableText = "set",
        },
        new()
        {
            Id = 2, Bool = false, SByte = sbyte.MinValue, Byte = byte.MaxValue, Short = short.MinValue, UShort = ushort.MaxValue,
            UInt = uint.MaxValue, Long = long.MaxValue, ULong = ulong.MaxValue, Float = float.MaxValue, Double = double.MinValue,
            Decimal = -99_999_999_999_999.9999m, Text = "quote ' backslash \\ newline \n unicode é漢", Char = '\'',
            Date = new DateOnly(1, 1, 1), DateTime = new DateTime(9999, 12, 31, 23, 59, 59), DateTimeOffset = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero),
            TimeOnly = TimeOnly.MaxValue, TimeSpan = TimeSpan.FromMinutes(-90), Bytes = [], Guid = Guid.Empty,
            NullableInt = null, NullableText = null,
        },
    ];

    public static IReadOnlyList<BulkGroup> BulkGroups() =>
    [
        new() { Id = 1, Region = "north" },
        new() { Id = 2, Region = "south" },
    ];

    public static Customer CopyWithoutOrders(Customer customer) => new()
    {
        Id = customer.Id,
        Name = customer.Name,
        City = customer.City,
        IsActive = customer.IsActive,
        Address = new Address { Street = customer.Address.Street, PostCode = customer.Address.PostCode },
    };

    public static Order Copy(Order order) => new()
    {
        Id = order.Id,
        CustomerId = order.CustomerId,
        Amount = order.Amount,
        Quantity = order.Quantity,
        OrderDate = order.OrderDate,
        Status = order.Status,
    };
}
