using Tnzi.AI.Device;
using Tnzi.AI.Device.Models;
using Tnzi.AI.Device.Options;
using Tnzi.AI.Device.Services;
using Tnzi.Exceptions;

namespace Tnzi.AI.Tests.Device;

public class DevicePairingServiceTests
{
    private readonly DevicePairingService _service;
    private readonly DeviceOptions _options;

    public DevicePairingServiceTests()
    {
        _options = new DeviceOptions
        {
            PairingCodeLength = 8,
            PairingCodeTtlMinutes = 60,
            MaxPendingPairings = 5
        };
        _service = new DevicePairingService(
            Microsoft.Extensions.Options.Options.Create(_options),
            NullLogger<DevicePairingService>.Instance);
    }

    [Fact]
    public async Task CreatePairingRequest_GeneratesCode()
    {
        var request = await _service.CreatePairingRequestAsync("node-1", "Test Device", DevicePlatform.Windows);

        request.ShouldNotBeNull();
        request.Code.ShouldNotBeNullOrWhiteSpace();
        request.Code.Length.ShouldBe(8);
        request.NodeId.ShouldBe("node-1");
        request.DeviceName.ShouldBe("Test Device");
    }

    [Fact]
    public async Task CreatePairingRequest_CodeExcludesConfusingChars()
    {
        // 生成多个 code 验证不包含容易混淆的字符
        var confusingChars = new HashSet<char> { 'I', 'O', 'S', 'Z', '0', '1' };
        var options = new DeviceOptions { PairingCodeLength = 16, MaxPendingPairings = 100 };
        var service = new DevicePairingService(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<DevicePairingService>.Instance);

        for (var i = 0; i < 50; i++)
        {
            var request = await service.CreatePairingRequestAsync($"node-{i}", "Test", DevicePlatform.Windows);
            foreach (var ch in request.Code)
            {
                confusingChars.ShouldNotContain(ch, $"Code '{request.Code}' contains confusing character '{ch}'");
            }
        }
    }

    [Fact]
    public async Task ApprovePairing_MarksApproved()
    {
        var request = await _service.CreatePairingRequestAsync("node-1", "Test Device", DevicePlatform.Windows);

        var info = await _service.ApprovePairingAsync(request.Code);

        info.ShouldNotBeNull();
        info.NodeId.ShouldBe("node-1");
        (await _service.IsApprovedAsync("node-1")).ShouldBeTrue();
    }

    [Fact]
    public async Task RejectPairing_RemovesRequest()
    {
        var request = await _service.CreatePairingRequestAsync("node-1", "Test Device", DevicePlatform.Windows);

        var result = await _service.RejectPairingAsync(request.Code);

        result.ShouldBeTrue();
        var pending = await _service.GetPendingRequestsAsync();
        pending.Count.ShouldBe(0);
    }

    [Fact]
    public async Task RevokeDevice_RemovesApproval()
    {
        var request = await _service.CreatePairingRequestAsync("node-1", "Test Device", DevicePlatform.Windows);
        await _service.ApprovePairingAsync(request.Code);

        var result = await _service.RevokeDeviceAsync("node-1");

        result.ShouldBeTrue();
        (await _service.IsApprovedAsync("node-1")).ShouldBeFalse();
    }

    [Fact]
    public async Task MaxPendingPairings_Enforced()
    {
        var options = new DeviceOptions { MaxPendingPairings = 2 };
        var service = new DevicePairingService(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<DevicePairingService>.Instance);

        await service.CreatePairingRequestAsync("node-1", "Dev1", DevicePlatform.Windows);
        await service.CreatePairingRequestAsync("node-2", "Dev2", DevicePlatform.MacOS);

        var ex = await Should.ThrowAsync<BusinessException>(
            () => service.CreatePairingRequestAsync("node-3", "Dev3", DevicePlatform.Linux));
        ex.HttpStatusCode.ShouldBe(429);
    }

    [Fact]
    public async Task ApprovePairing_InvalidCode_ReturnsNull()
    {
        var result = await _service.ApprovePairingAsync("INVALID");

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetPendingRequests_ReturnsAll()
    {
        await _service.CreatePairingRequestAsync("node-1", "Dev1", DevicePlatform.Windows);
        await _service.CreatePairingRequestAsync("node-2", "Dev2", DevicePlatform.MacOS);

        var pending = await _service.GetPendingRequestsAsync();

        pending.Count.ShouldBe(2);
    }
}
