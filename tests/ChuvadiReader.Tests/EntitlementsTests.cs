using ChuvadiReader.Core.Licensing;
using Xunit;

namespace ChuvadiReader.Tests;

/// <summary>Guards the entitlements seam. Today every capability is on; when real licensing
/// replaces <see cref="DefaultEntitlements"/>, this is the contract the default must keep.</summary>
public class EntitlementsTests
{
    [Fact]
    public void Default_CanRedact_IsTrue()
    {
        IEntitlements entitlements = new DefaultEntitlements();
        Assert.True(entitlements.CanRedact);
    }
}
