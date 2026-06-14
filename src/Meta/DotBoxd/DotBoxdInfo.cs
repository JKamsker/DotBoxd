namespace DotBoxd;

/// <summary>
/// Marker type for the <c>DotBoxd</c> meta-package. The package itself carries no logic; it exists to
/// pull in the full DotBoxd stack across the three usage modes:
/// <list type="bullet">
///   <item><description><b>Services</b> — source-generated RPC proxies and dispatchers (<c>DotBoxd.Services</c>).</description></item>
///   <item><description><b>Kernels</b> — safe, sandboxed logic validated and executed under a policy (<c>DotBoxd.Kernels</c> + <c>DotBoxd.Hosting</c>).</description></item>
///   <item><description><b>Pushdown</b> — running kernels next to host services so a client submits one request instead of many (<c>DotBoxd.Pushdown.Services</c>).</description></item>
/// </list>
/// </summary>
public static class DotBoxdInfo
{
    /// <summary>The three usage modes bundled by this meta-package.</summary>
    public const string Modes = "Services, Kernels, Pushdown";
}
