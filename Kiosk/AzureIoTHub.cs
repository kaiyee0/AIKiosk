using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;

static class AzureIoTHub
{
    //
    // Note: this connection string is specific to the device "Kiosk". To configure other devices,
    // see information on iothub-explorer at http://aka.ms/iothubgetstartedVSCS
    //
    const string deviceConnectionString = "HostName=CognitiveHub.azure-devices.net;DeviceId=Kiosk;SharedAccessKey=uW0L6l8fFgpvYEUSv2nAH1Y+tbEGXJjoms5XqCz2eGg=";

    //
    // To monitor messages sent to device "Kiosk" use iothub-explorer as follows:
    //    iothub-explorer HostName=CognitiveHub.azure-devices.net;SharedAccessKeyName=service;SharedAccessKey=P5mNiZJ/cHFO1Irf6BIR6XP3+amaaZvzmgVT8Rzdv/c= monitor-events "Kiosk"
    //

    // Refer to http://aka.ms/azure-iot-hub-vs-cs-wiki for more information on Connected Service for Azure IoT Hub

    public static async Task SendDeviceToCloudMessageAsync()
    {
        var deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Amqp);

#if WINDOWS_UWP
        var str = "Hello, Cloud from a UWP C# app!";
#else
        var str = "Hello, Cloud from a C# app!";
#endif
        var message = new Message(Encoding.ASCII.GetBytes(str));

        await deviceClient.SendEventAsync(message);
    }

    public static async Task SendSQLToCloudMessageAsync(string str)
    {
        var deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Amqp);
        var message = new Message(Encoding.ASCII.GetBytes(str));
        await deviceClient.SendEventAsync(message);
    }

    public static async Task<string> ReceiveCloudToDeviceMessageAsync()
    {
        var deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Amqp);

        while (true)
        {
            var receivedMessage = await deviceClient.ReceiveAsync();

            if (receivedMessage != null)
            {
                var messageData = Encoding.ASCII.GetString(receivedMessage.GetBytes());
                await deviceClient.CompleteAsync(receivedMessage);
                return messageData;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}
