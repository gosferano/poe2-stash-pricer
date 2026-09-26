using System;

namespace Poe2StashPricer.App.Platform.Linux;

/// <summary>
/// The data-control protocol, hand-written because there is no generated code here.
///
/// It exists twice with the same shape: wlr-data-control-unstable-v1, which wlroots compositors have carried
/// for years, and ext-data-control-v1, the standardised version that replaced it. Only the names differ, so
/// both are built from one description and whichever the compositor offers is used.
///
/// This is the protocol clipboard managers use: it reads and sets the selection without the app needing
/// focus, which is exactly what putting the user's clipboard back requires.
/// </summary>
internal sealed class DataControl
{
    public WlInterface Manager { get; }
    public WlInterface Device { get; }
    public WlInterface Source { get; }
    public WlInterface Offer { get; }

    // Request opcodes, the same in both versions of the protocol.
    public const uint ManagerCreateDataSource = 0, ManagerGetDataDevice = 1, ManagerDestroy = 2;
    public const uint DeviceSetSelection = 0, DeviceDestroy = 1;
    public const uint SourceOffer = 0, SourceDestroy = 1;
    public const uint OfferReceive = 0, OfferDestroy = 1;

    // Event indexes, in the order the listener arrays must be in.
    public const int DeviceDataOffer = 0, DeviceSelection = 1, DeviceFinished = 2, DevicePrimarySelection = 3;
    public const int SourceSend = 0, SourceCancelled = 1;

    /// <summary>The newest version of the device interface this describes.</summary>
    public uint DeviceVersion { get; }

    private DataControl(string prefix, uint deviceVersion)
    {
        DeviceVersion = deviceVersion;
        IntPtr nil = IntPtr.Zero;

        Offer = WlInterface.Create(prefix + "_data_control_offer_v1", 1,
            new[]
            {
                new WlInterface.Message("receive", "sh", new[] { nil, nil }),
                new WlInterface.Message("destroy", "", null),
            },
            new[]
            {
                new WlInterface.Message("offer", "s", new[] { nil }),
            });

        Source = WlInterface.Create(prefix + "_data_control_source_v1", 1,
            new[]
            {
                new WlInterface.Message("offer", "s", new[] { nil }),
                new WlInterface.Message("destroy", "", null),
            },
            new[]
            {
                new WlInterface.Message("send", "sh", new[] { nil, nil }),
                new WlInterface.Message("cancelled", "", null),
            });

        // primary_selection is there from the start in ext-, and only from version 2 in the wlroots one.
        // Declaring it either way is safe: a server bound at v1 simply never sends it.
        Device = WlInterface.Create(prefix + "_data_control_device_v1", (int)deviceVersion,
            new[]
            {
                new WlInterface.Message("set_selection", "?o", new[] { Source.Ptr }),
                new WlInterface.Message("destroy", "", null),
                new WlInterface.Message("set_primary_selection", "2?o", new[] { Source.Ptr }),
            },
            new[]
            {
                new WlInterface.Message("data_offer", "n", new[] { Offer.Ptr }),
                new WlInterface.Message("selection", "?o", new[] { Offer.Ptr }),
                new WlInterface.Message("finished", "", null),
                new WlInterface.Message("primary_selection", "?o", new[] { Offer.Ptr }),
            });

        Manager = WlInterface.Create(prefix + "_data_control_manager_v1", (int)deviceVersion,
            new[]
            {
                new WlInterface.Message("create_data_source", "n", new[] { Source.Ptr }),
                new WlInterface.Message("get_data_device", "no", new[] { Device.Ptr, Wl.SeatInterface }),
                new WlInterface.Message("destroy", "", null),
            },
            Array.Empty<WlInterface.Message>());
    }

    /// <summary>The standardised protocol first, then the wlroots one it grew out of.</summary>
    public static DataControl Ext { get; } = new DataControl("ext", 1);

    public static DataControl Wlr { get; } = new DataControl("zwlr", 2);
}
