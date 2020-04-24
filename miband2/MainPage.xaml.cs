using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Exceptions;
using Xamarin.Forms;

namespace miband2
{
    // useful link: https://stackoverflow.com/questions/55746274/how-to-get-heart-rate-from-mi-band-using-bluetooth-le
    // https://www.oreilly.com/library/view/getting-started-with/9781491900550/ch04.html#gatt_cccd
    // https://github.com/aashari/mi-band-2
    [DesignTimeVisible(false)]
    public partial class MainPage : ContentPage
    {
        private IBluetoothLE _ble;
        private IDevice _braceletDevice;

        // this UUID is standard for Bluetooth LE
        private readonly Guid HEART_RATE_MEASUREMENT_CHARACTERISTIC = new Guid("00002a37-0000-1000-8000-00805f9b34fb");
        private readonly Guid HEART_RATE_CONTROLPOINT_CHARACTERISTIC = new Guid("00002a39-0000-1000-8000-00805f9b34fb");
        private readonly Guid HEART_RATE_SERVICE = new Guid("0000180d-0000-1000-8000-00805f9b34fb");
        private const byte COMMAND_BYTE = 21;
        private const byte COMMAND_ON = 1;
        private const byte COMMAND_OFF = 0;
        private const byte COMMAND_HEART_RATE_CONTINUOUS = 1;
        private const byte COMMAND_HEART_RATE_MANUAL = 2;
        private int _lastHeartRate;

        public MainPage()
        {
            InitializeComponent();
            editor.Text = "Press Click!";
            _ble = CrossBluetoothLE.Current;
        }

        private void ResolveBracelet()
        {
            var connected =
                _ble.Adapter.GetSystemConnectedOrPairedDevices(new[] {HEART_RATE_SERVICE});
            foreach (var device in connected)
            {
                if (string.IsNullOrEmpty(device.Name))
                    continue;
                editor.Text += $"\n Found device: {device.Name}";
                if (device.Name != "MI Band 2" || _braceletDevice != null) continue;
                // we are stopping scanning
                _ble.Adapter.StopScanningForDevicesAsync();
                _braceletDevice = device;
                editor.Text += $"\n Bracelet detected, connecting...\n";
                break;
            }

            ProcessBracelet();
            // Task.Factory.StartNew(async () => { await ProcessBracelet(); },
            //     CancellationToken.None, TaskCreationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private async Task ProcessBracelet()
        {
            try
            {
                await _ble.Adapter.ConnectToDeviceAsync(_braceletDevice);
                var sb = new StringBuilder("Connected...\n");
                var services = await _braceletDevice.GetServicesAsync();
                sb.AppendLine($"Services found on this device: {string.Join(", ", services.Select(s => s.Name))}");
                // As we now: we have two known services: device information and heart rate service.
                // That services have description as standard Bluetooth LE services, links:
                // All services: https://www.bluetooth.com/specifications/gatt/services/
                // Device information service: https://www.bluetooth.org/docman/handlers/downloaddoc.ashx?doc_id=244369
                // Heart rate service: https://www.bluetooth.org/docman/handlers/downloaddoc.ashx?doc_id=239866
                // Alert Notification Service: https://www.bluetooth.org/docman/handlers/downloaddoc.ashx?doc_id=242287
                var deviceInfo =
                    await ProcessDeviceInformationService(services.First(s => s.Name == "Device Information"));
                sb.AppendLine(deviceInfo);
                editor.Text += sb.ToString();

                // processing heart rate
                await ProcessHeartRate(services.First(s => s.Name == "Heart Rate"));
            }
            catch (DeviceConnectionException e)
            {
                editor.Text += $"\n Error";
            }
        }

        private async Task ProcessHeartRate(IService heartRateService)
        {
            try
            {
                await _ble.Adapter.ConnectToDeviceAsync(_braceletDevice);
                var sb = new StringBuilder("Measuring heart rate...\n");
                var measurementCharacteristic =
                    await heartRateService.GetCharacteristicAsync(HEART_RATE_MEASUREMENT_CHARACTERISTIC);
                measurementCharacteristic.ValueUpdated += MeasurementCharacteristicOnValueUpdated;
                var controlCharacteristic =
                    await heartRateService.GetCharacteristicAsync(HEART_RATE_CONTROLPOINT_CHARACTERISTIC);
                await controlCharacteristic.WriteAsync(new byte[]
                    {COMMAND_BYTE, COMMAND_HEART_RATE_CONTINUOUS, COMMAND_ON});
                measurementCharacteristic =
                    await heartRateService.GetCharacteristicAsync(HEART_RATE_MEASUREMENT_CHARACTERISTIC);
                await measurementCharacteristic.StartUpdatesAsync();
                editor.Text += sb.ToString();
            }
            catch (Exception e)
            {
                await DisplayAlert("Error", e.Message, "OK");
                Console.WriteLine(e);
            }
        }

        private void MeasurementCharacteristicOnValueUpdated(object sender, CharacteristicUpdatedEventArgs e)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                editor.Text += $"❤️: {e.Characteristic.Value[1]} \n";
            });
        }

        private async Task<string> ProcessDeviceInformationService(IService deviceInfoService)
        {
            var sb = new StringBuilder("Getting information from Device Information service: \n");
            var characteristics = await deviceInfoService.GetCharacteristicsAsync();
            foreach (var characteristic in characteristics)
            {
                var characteristicData = await characteristic.ReadAsync();
                sb.AppendLine(
                    $"- {characteristic.Name}: {Encoding.UTF8.GetString(characteristicData, 0, characteristicData.Length)}");
            }

            return sb.ToString();
        }

        // bluetooth le lib here: https://github.com/xabre/xamarin-bluetooth-le
        // this app based from code located here: https://github.com/AL3X1/Mi-Band-2-SDK
        // more about Bluetooth GATT: https://www.bluetooth.com/specifications/gatt/
        void click_Clicked(object sender, System.EventArgs e)
        {
            SetupBluetooth();
            clickButton.IsEnabled = false;
        }

        void SetupBluetooth()
        {
            var stringB = new StringBuilder();
            stringB.AppendLine("Checking bluetooth... Please enable bluetooth if disabled.");
            while (_ble.State != BluetoothState.On)
            {
                Thread.Sleep(2000);
            }

            stringB.AppendLine($"BT state is On");
            stringB.AppendLine($"Watching connected devices...");
            editor.Text = stringB.ToString();
            ResolveBracelet();
            // clickButton.IsEnabled = true;
        }
    }
}