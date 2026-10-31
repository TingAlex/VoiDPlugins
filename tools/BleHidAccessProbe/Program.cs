using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

const ulong FunctionTrackpadAddress = 0xC98C17AE7054;
Guid hidServiceId = new("00001812-0000-1000-8000-00805F9B34FB");

Console.WriteLine($"Opening BLE device {FunctionTrackpadAddress:X12}...");
using BluetoothLEDevice? device =
    await BluetoothLEDevice.FromBluetoothAddressAsync(FunctionTrackpadAddress);

if (device is null)
{
    Console.WriteLine("RESULT: BluetoothLEDevice could not be opened.");
    return 2;
}

Console.WriteLine($"Name: {device.Name}");
Console.WriteLine($"ConnectionStatus: {device.ConnectionStatus}");
Console.WriteLine($"DeviceId: {device.DeviceId}");

GattDeviceServicesResult services =
    await device.GetGattServicesForUuidAsync(hidServiceId, BluetoothCacheMode.Uncached);
Console.WriteLine($"HID service query: {services.Status}");
Console.WriteLine($"ProtocolError: {services.ProtocolError?.ToString() ?? "none"}");
Console.WriteLine($"Service count: {services.Services.Count}");

foreach (GattDeviceService service in services.Services)
{
    using (service)
    {
        DeviceAccessStatus access = await service.RequestAccessAsync();
        Console.WriteLine(
            $"Service {service.Uuid}; requestAccess={access}; "
            + $"device={service.Session.DeviceId.Id}");
        GattCharacteristicsResult characteristics =
            await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
        Console.WriteLine(
            $"  Characteristics: status={characteristics.Status}; "
            + $"protocolError={characteristics.ProtocolError?.ToString() ?? "none"}; "
            + $"count={characteristics.Characteristics.Count}");

        foreach (GattCharacteristic characteristic in characteristics.Characteristics)
        {
            Console.WriteLine(
                $"  {characteristic.Uuid} properties={characteristic.CharacteristicProperties}");
        }
    }
}

return services.Status == GattCommunicationStatus.Success ? 0 : 1;
