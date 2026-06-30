namespace BricksNet.IntegrationTests.FakeService;

/// <summary>
/// Operating mode of the in-process fake Databricks service, mirroring the record/replay pattern
/// used by the Java driver's <c>fakeservice</c> integration tests.
/// </summary>
public enum FakeServiceMode
{
    /// <summary>
    /// Forward each request to a real workspace and persist the request/response pair as a fixture.
    /// Requires live credentials and is only used to (re)capture fixtures; never runs in CI.
    /// </summary>
    Record,

    /// <summary>
    /// Serve responses from previously captured fixtures without any network access.
    /// This is the default mode for the offline integration test suite.
    /// </summary>
    Replay,

    /// <summary>
    /// Forward each request to a real workspace without persisting anything. Requires live
    /// credentials; used for ad-hoc debugging against a live endpoint.
    /// </summary>
    Passthrough,
}
