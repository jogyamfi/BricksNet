using System.Text;
using BricksNet.Backends.Thrift.Generated;
using Thrift;
using Thrift.Transport;

namespace BricksNet.Backends.Thrift;

// -------------------------------------------------------------------------
// Thrift binary-protocol encoding helpers for unit tests.
//
// Produces the exact bytes that TBinaryProtocol (strict-write) emits for a
// REPLY message, so FakeThriftTransport can serve them without a real server.
//
// TType wire codes used below:
//   Stop = 0x00   Bool = 0x02   I32 = 0x08   I64 = 0x0A
//   String/Binary = 0x0B        Struct = 0x0C
// -------------------------------------------------------------------------

internal static class ThriftBinaryEncoder
{
    // ── public builders ──────────────────────────────────────────────────

    /// <summary>Status-only reply (CloseSession, CancelOperation, CloseOperation, etc.).</summary>
    public static byte[] StatusOnlyFrame(string method, int seqId = 0) =>
        Frame(method, seqId, [
            .. StructField(1, SuccessStatus()),  // field 1 = status
        ]);

    /// <summary>OpenSession reply with a session handle carrying <paramref name="guid"/>.</summary>
    public static byte[] OpenSessionFrame(string method, Guid guid, int seqId = 0) =>
        Frame(method, seqId, [
            .. StructField(1, SuccessStatus()),          // field 1 = status
            .. I32Field(2, 42248),                       // field 2 = serverProtocolVersion (SparkV8)
            .. StructField(3, SessionHandleBytes(guid)), // field 3 = sessionHandle
        ]);

    /// <summary>ExecuteStatement / GetCatalogs / GetSchemas / GetTables / GetColumns reply.</summary>
    public static byte[] ExecuteStatementFrame(string method, Guid opGuid, int seqId = 0) =>
        Frame(method, seqId, [
            .. StructField(1, SuccessStatus()),              // field 1 = status
            .. StructField(2, OperationHandleBytes(opGuid)), // field 2 = operationHandle
        ]);

    /// <summary>GetOperationStatus reply with the given operation state.</summary>
    public static byte[] GetOperationStatusFrame(string method, TOperationState state, int seqId = 0) =>
        Frame(method, seqId, [
            .. StructField(1, SuccessStatus()),   // field 1 = status
            .. I32Field(2, (int)state),           // field 2 = operationState
        ]);

    /// <summary>FetchResults reply with an empty result set (no rows).</summary>
    public static byte[] FetchResultsEmptyFrame(string method, int seqId = 0) =>
        Frame(method, seqId, [
            .. StructField(1, SuccessStatus()),  // field 1 = status
            // hasMoreRows + results omitted → Results=null in TFetchResultsResp
        ]);

    /// <summary>GetResultSetMetadata reply (status only; schema is optional).</summary>
    public static byte[] GetResultSetMetadataFrame(string method, int seqId = 0) =>
        StatusOnlyFrame(method, seqId);

    // ── internal helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Wraps <paramref name="innerFields"/> in the full TBinaryProtocol frame:
    /// MessageHeader(Reply) → outer struct → field 0 (success) → inner → Stop.
    /// </summary>
    private static byte[] Frame(string method, int seqId, byte[] innerFields)
    {
        var ms = new MemoryStream();

        // MessageBegin (strict / VERSION_1 format = 0x80010002 for Reply)
        WriteI32(ms, unchecked((int)0x80010002));
        WriteString(ms, method);
        WriteI32(ms, seqId);

        // outer "result" struct  →  field 0 = "success" (Struct)
        ms.WriteByte(0x0C);      // TType.Struct
        WriteI16(ms, 0);         // field id 0

        // innerFields = the field list of the response struct (no terminating Stop).
        ms.Write(innerFields);
        ms.WriteByte(0x00);      // Stop for the response struct (TOpenSessionResp etc.)
        ms.WriteByte(0x00);      // Stop for the outer "result" struct

        // MessageEnd – no bytes in binary protocol
        return ms.ToArray();
    }

    // ── primitive struct writers ─────────────────────────────────────────

    private static byte[] SuccessStatus()
    {
        var ms = new MemoryStream();
        ms.WriteByte(0x08);       // TType.I32
        WriteI16(ms, 1);          // field 1 = StatusCode
        WriteI32(ms, 0);          // TStatusCode.SuccessStatus = 0
        ms.WriteByte(0x00);       // Stop
        return ms.ToArray();
    }

    private static byte[] HandleIdentifierBytes(Guid guid)
    {
        var guidBytes = guid.ToByteArray();  // 16 bytes
        var ms = new MemoryStream();
        ms.WriteByte(0x0B);          // TType.String (binary)
        WriteI16(ms, 1);             // field 1 = guid
        WriteI32(ms, 16);
        ms.Write(guidBytes);
        ms.WriteByte(0x0B);          // TType.String (binary)
        WriteI16(ms, 2);             // field 2 = secret
        WriteI32(ms, 16);
        ms.Write(guidBytes);         // reuse same bytes as secret
        ms.WriteByte(0x00);          // Stop
        return ms.ToArray();
    }

    private static byte[] SessionHandleBytes(Guid guid)
    {
        var ms = new MemoryStream();
        ms.WriteByte(0x0C);                    // TType.Struct
        WriteI16(ms, 1);                       // field 1 = sessionId
        ms.Write(HandleIdentifierBytes(guid)); // THandleIdentifier
        ms.WriteByte(0x00);                    // Stop
        return ms.ToArray();
    }

    private static byte[] OperationHandleBytes(Guid guid)
    {
        var ms = new MemoryStream();
        ms.WriteByte(0x0C);                    // TType.Struct
        WriteI16(ms, 1);                       // field 1 = operationId
        ms.Write(HandleIdentifierBytes(guid)); // THandleIdentifier
        ms.WriteByte(0x08);                    // TType.I32
        WriteI16(ms, 2);                       // field 2 = operationType
        WriteI32(ms, 0);                       // TOperationType.ExecuteStatement = 0
        ms.WriteByte(0x02);                    // TType.Bool
        WriteI16(ms, 3);                       // field 3 = hasResultSet
        ms.WriteByte(0x01);                    // true
        ms.WriteByte(0x00);                    // Stop
        return ms.ToArray();
    }

    // ── field-envelope helpers ────────────────────────────────────────────

    private static byte[] StructField(int fieldId, byte[] structBody)
    {
        var ms = new MemoryStream();
        ms.WriteByte(0x0C);   // TType.Struct
        WriteI16(ms, (short)fieldId);
        ms.Write(structBody);
        return ms.ToArray();
    }

    private static byte[] I32Field(int fieldId, int value)
    {
        var ms = new MemoryStream();
        ms.WriteByte(0x08);   // TType.I32
        WriteI16(ms, (short)fieldId);
        WriteI32(ms, value);
        return ms.ToArray();
    }

    // ── low-level big-endian writers ──────────────────────────────────────

    private static void WriteI16(Stream s, short value)
    {
        s.WriteByte((byte)((value >> 8) & 0xFF));
        s.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteI32(Stream s, int value)
    {
        s.WriteByte((byte)((value >> 24) & 0xFF));
        s.WriteByte((byte)((value >> 16) & 0xFF));
        s.WriteByte((byte)((value >> 8) & 0xFF));
        s.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteString(Stream s, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteI32(s, bytes.Length);
        s.Write(bytes);
    }
}

// -------------------------------------------------------------------------
// Fake Thrift transport that serves pre-encoded response frames from a queue.
//
// Usage:
//   var transport = new FakeThriftTransport(frame1, frame2, ...);
//   var protocol  = new TBinaryProtocol(transport);
//   var client    = new TCLIServiceClient(protocol);
//   // client.SomeAsync() will consume frame1, then frame2, etc.
// -------------------------------------------------------------------------

internal sealed class FakeThriftTransport : TEndpointTransport
{
    private readonly Queue<byte[]> _frames;
    private MemoryStream _readBuffer = new();
    private bool _isOpen = true;

    public FakeThriftTransport(params byte[][] frames)
        : base(new TConfiguration { MaxMessageSize = int.MaxValue })
    {
        _frames = new Queue<byte[]>(frames);
    }

    public override bool IsOpen => _isOpen;

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public override void Close()
    {
        _isOpen = false;
        _readBuffer.SetLength(0);
    }

    public override ValueTask<int> ReadAsync(byte[] buffer, int offset, int length, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var n = _readBuffer.Read(buffer, offset, length);
        CountConsumedMessageBytes(n);
        return new ValueTask<int>(n);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int length, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask; // discard the request
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResetConsumedMessageSize();

        if (_frames.TryDequeue(out var frame))
        {
            _readBuffer = new MemoryStream(frame);
        }

        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _readBuffer.Dispose();
        }
        // TTransport.Dispose(bool) is abstract - do not call base
    }
}
